// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Specialization;

public class LinuxInstanceManagerContractTests : EnvironmentContractTestBase
{
    [Fact]
    public async Task AssignInstanceAsync_PreservesSequentialWritesPlatformContextAndFinallyOrder()
    {
        List<string> operations = [];
        ConfigureEnvironment(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [EnvironmentSettingNames.AzureWebsitePlaceholderMode] = "1",
            },
            operations);
        RecordingScriptWebHostEnvironment webHostEnvironment = new(
            new ScriptWebHostEnvironment(_testEnvironment),
            operations.Add);
        Mock<IMeshServiceClient> meshServiceClient = new(MockBehavior.Strict);
        using HttpClient client = new();
        ContractLinuxInstanceManager manager = CreateManager(
            webHostEnvironment,
            meshServiceClient.Object,
            client,
            _ =>
            {
                operations.Add("assignment:platform-context");
                Assert.True(webHostEnvironment.DelayRequestsEnabled);
                return Task.CompletedTask;
            });
        HostAssignmentContext context = CreateAssignmentContext();

        bool assigned = await manager.AssignInstanceAsync(context);

        Assert.True(assigned);
        Assert.Equal(
            [
                "assignment:delay-requests",
                "write:payload-setting=payload-value",
                $"write:{EnvironmentSettingNames.CorsSupportCredentials}=True",
                $"write:{EnvironmentSettingNames.CorsAllowedOrigins}=[\"https://example.test\"]",
                $"write:{EnvironmentSettingNames.EasyAuthEnabled}=True",
                $"write:{EnvironmentSettingNames.EasyAuthClientId}=site-client-id",
                $"write:{EnvironmentSettingNames.FunctionsSiteUpdateId}=42",
                "assignment:platform-context",
                "assignment:flag-specialized-ready",
                $"write:{EnvironmentSettingNames.AzureWebsitePlaceholderMode}=0",
                $"write:{EnvironmentSettingNames.AzureWebsiteContainerReady}=1",
                "assignment:resume-requests",
            ],
            RelevantOperations(operations));
        Assert.Equal("0", _testEnvironment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode));
        Assert.Equal("1", _testEnvironment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady));
        Assert.False(webHostEnvironment.DelayRequestsEnabled);
        Assert.True(webHostEnvironment.DelayCompletionTask.IsCompleted);
        Assert.False(webHostEnvironment.InStandbyMode);

        _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
        Assert.False(webHostEnvironment.InStandbyMode);
    }

    [Fact]
    public async Task AssignInstanceAsync_SetterFailureLeavesPartialWritesAndStillCompletesFinally()
    {
        List<string> operations = [];
        Dictionary<string, string> initialValues = new(StringComparer.Ordinal)
        {
            [EnvironmentSettingNames.AzureWebsitePlaceholderMode] = "1",
            [EnvironmentSettingNames.EasyAuthClientId] = "old-client-id",
        };
        RecordingProcessMutator mutator = ConfigureEnvironment(initialValues, operations);
        mutator.FailureName = EnvironmentSettingNames.EasyAuthClientId;
        RecordingScriptWebHostEnvironment webHostEnvironment = new(
            new ScriptWebHostEnvironment(_testEnvironment),
            operations.Add);
        Mock<IMeshServiceClient> meshServiceClient = new(MockBehavior.Strict);
        meshServiceClient
            .Setup(client => client.NotifyHealthEvent(
                ContainerHealthEventType.Fatal,
                typeof(ContractLinuxInstanceManager),
                "Assign failed"))
            .Callback(() => operations.Add("assignment:fatal-health-event"))
            .Returns(Task.CompletedTask);
        bool platformContextApplied = false;
        using HttpClient client = new();
        ContractLinuxInstanceManager manager = CreateManager(
            webHostEnvironment,
            meshServiceClient.Object,
            client,
            _ =>
            {
                platformContextApplied = true;
                return Task.CompletedTask;
            });

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.AssignInstanceAsync(CreateAssignmentContext()));

        Assert.Equal(
            $"Mutation failed for '{EnvironmentSettingNames.EasyAuthClientId}'.",
            exception.Message);
        Assert.False(platformContextApplied);
        Assert.Equal(
            [
                "assignment:delay-requests",
                "write:payload-setting=payload-value",
                $"write:{EnvironmentSettingNames.CorsSupportCredentials}=True",
                $"write:{EnvironmentSettingNames.CorsAllowedOrigins}=[\"https://example.test\"]",
                $"write:{EnvironmentSettingNames.EasyAuthEnabled}=True",
                $"write:{EnvironmentSettingNames.EasyAuthClientId}=site-client-id",
                "assignment:fatal-health-event",
                "assignment:flag-specialized-ready",
                $"write:{EnvironmentSettingNames.AzureWebsitePlaceholderMode}=0",
                $"write:{EnvironmentSettingNames.AzureWebsiteContainerReady}=1",
                "assignment:resume-requests",
            ],
            RelevantOperations(operations));
        Assert.Equal("payload-value", _testEnvironment.GetEnvironmentVariable("payload-setting"));
        Assert.Equal(bool.TrueString, _testEnvironment.GetEnvironmentVariable(EnvironmentSettingNames.EasyAuthEnabled));
        Assert.Equal("old-client-id", _testEnvironment.GetEnvironmentVariable(EnvironmentSettingNames.EasyAuthClientId));
        Assert.Null(_testEnvironment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsSiteUpdateId));
        Assert.Equal("0", _testEnvironment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode));
        Assert.Equal("1", _testEnvironment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady));
        Assert.False(webHostEnvironment.DelayRequestsEnabled);
        Assert.True(webHostEnvironment.DelayCompletionTask.IsCompleted);
        meshServiceClient.VerifyAll();
    }

    private static HostAssignmentContext CreateAssignmentContext()
    {
        return new HostAssignmentContext
        {
            Environment = new Dictionary<string, string>
            {
                ["payload-setting"] = "payload-value",
            },
            CorsSettings = new CorsSettings
            {
                AllowedOrigins = ["https://example.test"],
                SupportCredentials = true,
            },
            EasyAuthSettings = new EasyAuthSettings
            {
                SiteAuthEnabled = true,
                SiteAuthClientId = "site-client-id",
            },
            SiteUpdateId = 42,
        };
    }

    private ContractLinuxInstanceManager CreateManager(
        IScriptWebHostEnvironment webHostEnvironment,
        IMeshServiceClient meshServiceClient,
        HttpClient client,
        Func<HostAssignmentContext, Task> applyContext)
    {
        Mock<IHttpClientFactory> httpClientFactory = new(MockBehavior.Strict);
        httpClientFactory
            .Setup(factory => factory.CreateClient(It.IsAny<string>()))
            .Returns(client);

        return new ContractLinuxInstanceManager(
            this,
            httpClientFactory.Object,
            webHostEnvironment,
            meshServiceClient,
            applyContext);
    }

    private RecordingProcessMutator ConfigureEnvironment(
        IDictionary<string, string> values,
        List<string> operations)
    {
        _testEnvironment.Clear();
        foreach (KeyValuePair<string, string> pair in values)
        {
            _testEnvironment.SetEnvironmentVariable(pair.Key, pair.Value);
        }

        RecordingProcessMutator mutator = new(operations.Add);
        _testEnvironment.OnGetEnvironmentVariable =
            name => operations.Add($"read:{name}");
        _testEnvironment.OnSetEnvironmentVariable = mutator.Set;

        return mutator;
    }

    private static string[] RelevantOperations(IEnumerable<string> operations)
    {
        return operations
            .Where(operation => operation.StartsWith("assignment:", StringComparison.Ordinal)
                || operation.StartsWith("write:", StringComparison.Ordinal))
            .ToArray();
    }

    private sealed class ContractLinuxInstanceManager : LinuxInstanceManager
    {
        private readonly Func<HostAssignmentContext, Task> _applyContext;

        public ContractLinuxInstanceManager(
            LinuxInstanceManagerContractTests owner,
            IHttpClientFactory httpClientFactory,
            IScriptWebHostEnvironment webHostEnvironment,
            IMeshServiceClient meshServiceClient,
            Func<HostAssignmentContext, Task> applyContext)
            : base(
                httpClientFactory,
                webHostEnvironment,
                owner._testEnvironment,
                NullLogger<LinuxInstanceManager>.Instance,
                new TestMetricsLogger(),
                meshServiceClient)
        {
            _applyContext = applyContext;
        }

        public override Task<string> SpecializeMSISidecar(HostAssignmentContext context)
        {
            return Task.FromResult<string>(null);
        }

        public override Task<string> ValidateContext(HostAssignmentContext assignmentContext)
        {
            return Task.FromResult<string>(null);
        }

        protected override Task ApplyContextAsync(HostAssignmentContext assignmentContext)
        {
            return _applyContext(assignmentContext);
        }

        protected override Task<string> DownloadWarmupAsync(RunFromPackageContext context)
        {
            return Task.FromResult<string>(null);
        }
    }

    private sealed class RecordingScriptWebHostEnvironment : IScriptWebHostEnvironment
    {
        private readonly ScriptWebHostEnvironment _inner;
        private readonly Action<string> _record;

        public RecordingScriptWebHostEnvironment(
            ScriptWebHostEnvironment inner,
            Action<string> record)
        {
            _inner = inner;
            _record = record;
        }

        public bool DelayRequestsEnabled => _inner.DelayRequestsEnabled;

        public Task DelayCompletionTask => _inner.DelayCompletionTask;

        public bool InStandbyMode => _inner.InStandbyMode;

        public void DelayRequests()
        {
            _record("assignment:delay-requests");
            _inner.DelayRequests();
        }

        public void FlagAsSpecializedAndReady()
        {
            _record("assignment:flag-specialized-ready");
            _inner.FlagAsSpecializedAndReady();
        }

        public void ResumeRequests()
        {
            _record("assignment:resume-requests");
            _inner.ResumeRequests();
        }
    }
}
