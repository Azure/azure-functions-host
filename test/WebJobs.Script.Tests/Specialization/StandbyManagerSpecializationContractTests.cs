// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Specialization;

public class StandbyManagerSpecializationContractTests : EnvironmentContractTestBase
{
    [Fact]
    public async Task SpecializeHostCoreAsync_PreservesLiveMutationReloadResetTokenWorkerRestartAndReadinessOrder()
    {
        List<string> operations = [];
        ConfigureEnvironment(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [EnvironmentSettingNames.AzureWebsiteSku] = ScriptConstants.FlexConsumptionSku,
                [EnvironmentSettingNames.AzureWebJobsFeatureFlags] = ScriptConstants.FeatureFlagEnableMcpCustomHandlerPreview,
                [EnvironmentSettingNames.FunctionWorkerRuntime] = "dotnet-isolated",
                [EnvironmentSettingNames.AzureWebsiteHostName] = "placeholder.azurewebsites.net",
            },
            operations);
        HostNameProvider hostNameProvider = new(_testEnvironment);
        Assert.Equal("placeholder.azurewebsites.net", hostNameProvider.Value);
        _testEnvironment.SetEnvironmentVariable(
            EnvironmentSettingNames.AzureWebsiteHostName,
            "specialized.azurewebsites.net");
        operations.Clear();

        Mock<IConfigurationRoot> configuration = new(MockBehavior.Strict);
        configuration
            .Setup(root => root.Reload())
            .Callback(() =>
            {
                operations.Add("specialization:configuration-observed-live-mutation");
                Assert.Equal(
                    "custom",
                    _testEnvironment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime));
            });
        Mock<IWebHostWorkerManager> workerManager = new(MockBehavior.Strict);
        workerManager
            .Setup(manager => manager.SpecializeAsync())
            .Callback(() => operations.Add("specialization:worker-specialization"))
            .Returns(Task.CompletedTask);
        Mock<IScriptHostManager> scriptHostManager = new(MockBehavior.Strict);
        scriptHostManager
            .Setup(manager => manager.RestartHostAsync("Host specialization.", default))
            .Callback(() => operations.Add("specialization:script-host-restart"))
            .Returns(Task.CompletedTask);
        scriptHostManager
            .SetupGet(manager => manager.State)
            .Returns(() =>
            {
                operations.Add("specialization:readiness-wait");
                return ScriptHostState.Running;
            });

        StandbyManager.ResetChangeToken();
        IChangeToken initialChangeToken = StandbyManager.ChangeToken;
        using IDisposable tokenRegistration = initialChangeToken.RegisterChangeCallback(
            _ => operations.Add("specialization:standby-token-callback"),
            null);
        using RecordingStandbyManager manager = new(
            this,
            scriptHostManager.Object,
            workerManager.Object,
            configuration.Object,
            Mock.Of<IScriptWebHostEnvironment>(),
            Mock.Of<IOptionsMonitor<ScriptApplicationHostOptions>>(),
            hostNameProvider,
            Mock.Of<IHostApplicationLifetime>(),
            operations);

        try
        {
            await manager.SpecializeHostCoreAsync();

            Assert.Equal(
                [
                    "specialization:mcp-live-mutation",
                    $"write:{EnvironmentSettingNames.FunctionWorkerRuntime}=custom",
                    "specialization:timezone-clear",
                    "specialization:configuration-reload",
                    "specialization:configuration-observed-live-mutation",
                    "specialization:host-name-reset",
                    "specialization:shared-assembly-reset",
                    "specialization:standby-token-signal",
                    "specialization:standby-token-callback",
                    "specialization:worker-specialization",
                    "specialization:script-host-restart",
                    "specialization:readiness-wait",
                ],
                RelevantOperations(operations));
            Assert.Equal("specialized.azurewebsites.net", hostNameProvider.Value);
            Assert.True(initialChangeToken.HasChanged);
            Assert.Same(NullChangeToken.Singleton, StandbyManager.ChangeToken);
            configuration.VerifyAll();
            workerManager.VerifyAll();
            scriptHostManager.VerifyAll();
        }
        finally
        {
            StandbyManager.ResetChangeToken();
        }
    }

    private static string[] RelevantOperations(IEnumerable<string> operations)
    {
        return operations
            .Where(operation => operation.StartsWith("specialization:", StringComparison.Ordinal)
                || string.Equals(
                    operation,
                    $"write:{EnvironmentSettingNames.FunctionWorkerRuntime}=custom",
                    StringComparison.Ordinal))
            .ToArray();
    }

    private void ConfigureEnvironment(
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
    }

    private sealed class RecordingStandbyManager : StandbyManager
    {
        private readonly List<string> _operations;

        public RecordingStandbyManager(
            StandbyManagerSpecializationContractTests owner,
            IScriptHostManager scriptHostManager,
            IWebHostWorkerManager workerManager,
            IConfiguration configuration,
            IScriptWebHostEnvironment webHostEnvironment,
            IOptionsMonitor<ScriptApplicationHostOptions> options,
            HostNameProvider hostNameProvider,
            IHostApplicationLifetime applicationLifetime,
            List<string> operations)
            : base(
                scriptHostManager,
                workerManager,
                configuration,
                webHostEnvironment,
                owner._testEnvironment,
                options,
                NullLogger<StandbyManager>.Instance,
                hostNameProvider,
                applicationLifetime,
                new TestMetricsLogger())
        {
            _operations = operations;
        }

        internal override void ApplyMcpCustomHandlerSettings()
        {
            _operations.Add("specialization:mcp-live-mutation");
            base.ApplyMcpCustomHandlerSettings();
        }

        internal override void ClearTimeZoneCache()
        {
            _operations.Add("specialization:timezone-clear");
            base.ClearTimeZoneCache();
        }

        internal override void ReloadConfiguration()
        {
            _operations.Add("specialization:configuration-reload");
            base.ReloadConfiguration();
        }

        internal override void ResetHostNameProvider()
        {
            _operations.Add("specialization:host-name-reset");
            base.ResetHostNameProvider();
        }

        internal override void ResetSharedAssemblyContext()
        {
            _operations.Add("specialization:shared-assembly-reset");
            base.ResetSharedAssemblyContext();
        }

        internal override void SignalSpecializationChange()
        {
            _operations.Add("specialization:standby-token-signal");
            base.SignalSpecializationChange();
        }
    }
}
