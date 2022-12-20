// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Management.LinuxSpecialization;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Moq.Protected;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Managment
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.ContainerInstanceTests)]
    public class LegionInstanceManagerTests : IDisposable
    {
        private readonly TestLoggerProvider _loggerProvider;
        private readonly TestEnvironmentEx _environment;
        private readonly ScriptWebHostEnvironment _scriptWebEnvironment;
        private readonly LegionInstanceManager _instanceManager;
        private readonly Mock<IMeshServiceClient> _meshServiceClientMock;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly LoggerFactory _loggerFactory = new LoggerFactory();
        private readonly TestOptionsFactory<ScriptApplicationHostOptions> _optionsFactory = new TestOptionsFactory<ScriptApplicationHostOptions>(new ScriptApplicationHostOptions() { ScriptPath = Path.GetTempPath() });
        private readonly IRunFromPackageHandler _runFromPackageHandler;
        private readonly Mock<IPackageDownloadHandler> _packageDownloadHandler;

        public LegionInstanceManagerTests()
        {
            _httpClientFactory = TestHelpers.CreateHttpClientFactory();

            _loggerProvider = new TestLoggerProvider();
            _loggerFactory.AddProvider(_loggerProvider);

            _environment = new TestEnvironmentEx();
            _scriptWebEnvironment = new ScriptWebHostEnvironment(_environment);
            _meshServiceClientMock = new Mock<IMeshServiceClient>(MockBehavior.Strict);
            _packageDownloadHandler = new Mock<IPackageDownloadHandler>(MockBehavior.Strict);

            var metricsLogger = new MetricsLogger();
            var bashCommandHandler = new BashCommandHandler(metricsLogger, new Logger<BashCommandHandler>(_loggerFactory));
            var zipHandler = new UnZipHandler(metricsLogger, NullLogger<UnZipHandler>.Instance);
            _runFromPackageHandler = new RunFromPackageHandler(_environment, _meshServiceClientMock.Object,
                bashCommandHandler, zipHandler, _packageDownloadHandler.Object, metricsLogger, new Logger<RunFromPackageHandler>(_loggerFactory));

            _instanceManager = new LegionInstanceManager(_httpClientFactory, _scriptWebEnvironment, _environment,
                _loggerFactory.CreateLogger<LegionInstanceManager>(), new TestMetricsLogger(), _meshServiceClientMock.Object);

            _instanceManager.Reset();
        }

        [Fact]
        public async Task StartAssignment_AppliesAssignmentContext()
        {
            var envValue = new
            {
                Name = Path.GetTempFileName().Replace(".", string.Empty),
                Value = Guid.NewGuid().ToString()
            };
            var allowedOrigins = new string[]
            {
                "https://functions.azure.com",
                "https://functions-staging.azure.com",
                "https://functions-next.azure.com"
            };
            var supportCredentials = true;

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            var context = new HostAssignmentContext
            {
                Environment = new Dictionary<string, string>
                {
                    { envValue.Name, envValue.Value }
                },
                CorsSettings = new CorsSettings
                {
                    AllowedOrigins = allowedOrigins,
                    SupportCredentials = supportCredentials,
                },
                IsWarmupRequest = false
            };
            bool result = _instanceManager.StartAssignment(context);
            Assert.True(result);
            Assert.True(_scriptWebEnvironment.InStandbyMode);

            // specialization is done in the background
            await Task.Delay(500);

            var value = _environment.GetEnvironmentVariable(envValue.Name);
            Assert.Equal(value, envValue.Value);

            var supportCredentialsValue = _environment.GetEnvironmentVariable(EnvironmentSettingNames.CorsSupportCredentials);
            Assert.Equal(supportCredentialsValue, supportCredentials.ToString());

            var allowedOriginsValue = _environment.GetEnvironmentVariable(EnvironmentSettingNames.CorsAllowedOrigins);
            Assert.Equal(allowedOriginsValue, JsonConvert.SerializeObject(allowedOrigins));

            // verify logs
            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.StartsWith("Starting Assignment", p),
                p => Assert.StartsWith("Applying 1 app setting(s)", p),
                p => Assert.StartsWith("Triggering specialization", p));

            // calling again should return false, since we have 
            // already marked the container as specialized.
            _loggerProvider.ClearAllLogMessages();
            result = _instanceManager.StartAssignment(context);
            Assert.False(result);

            logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.StartsWith("Assign called while host is not in placeholder mode and start context is not present.", p));
        }

        [Fact]
        public async Task StartAssignment_Failure_ExitsPlaceholderMode()
        {
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            var context = new HostAssignmentContext
            {
                Environment = new Dictionary<string, string>
                {
                    // force the assignment to fail
                    { "throw", "test" }
                },
                IsWarmupRequest = false
            };

            _meshServiceClientMock.Setup(c => c.NotifyHealthEvent(ContainerHealthEventType.Fatal,
                It.Is<Type>(t => t == typeof(LegionInstanceManager)), "Assign failed")).Returns(Task.CompletedTask);

            bool result = _instanceManager.StartAssignment(context);
            Assert.True(result);
            Assert.True(_scriptWebEnvironment.InStandbyMode);

            await TestHelpers.Await(() => !_scriptWebEnvironment.InStandbyMode, timeout: 5000);

            var error = _loggerProvider.GetAllLogMessages().First(p => p.Level == LogLevel.Error);
            Assert.Equal("Assign failed", error.FormattedMessage);
            Assert.Equal("Kaboom!", error.Exception.Message);

            _meshServiceClientMock.Verify(c => c.NotifyHealthEvent(ContainerHealthEventType.Fatal,
                It.Is<Type>(t => t == typeof(LegionInstanceManager)), "Assign failed"), Times.Once);
        }

        [Fact]
        public async Task StartAssignment_Succeeds_With_No_RunFromPackage_AppSetting()
        {
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            var context = new HostAssignmentContext
            {
                Environment = new Dictionary<string, string>(),
                IsWarmupRequest = false
            };
            bool result = _instanceManager.StartAssignment(context);
            Assert.True(result);
            Assert.True(_scriptWebEnvironment.InStandbyMode);

            await TestHelpers.Await(() => !_scriptWebEnvironment.InStandbyMode, timeout: 5000);

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.StartsWith("Starting Assignment", p),
                p => Assert.StartsWith("Applying 0 app setting(s)", p),
                p => Assert.StartsWith("Triggering specialization", p));
        }

        [Fact]
        public async void StartAssignment_Does_Not_Assign_Settings_For_Warmup_Request()
        {

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            var context = new HostAssignmentContext
            {
                Environment = new Dictionary<string, string>(),
                IsWarmupRequest = true
            };
            bool result = _instanceManager.StartAssignment(context);

            Assert.True(result);
            await TestHelpers.Await(() => _scriptWebEnvironment.InStandbyMode, timeout: 5000);

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.False(logs.Any(l => l.StartsWith("Starting Assignment.")));
        }

        [Fact]
        public void StartAssignment_ReturnsTrue_ForPinnedContainers()
        {
            Assert.False(SystemEnvironment.Instance.IsPlaceholderModeEnabled());

            var context = new HostAssignmentContext();
            context.Environment = new Dictionary<string, string>()
            {
                { EnvironmentSettingNames.ContainerStartContext, "startContext" }
            };
            context.IsWarmupRequest = false;
            bool result = _instanceManager.StartAssignment(context);
            Assert.True(result);
        }

        [Fact]
        public void StartAssignment_ReturnsFalse_ForNonPinnedContainersInStandbyMode()
        {
            Assert.False(SystemEnvironment.Instance.IsPlaceholderModeEnabled());

            var context = new HostAssignmentContext();
            context.Environment = new Dictionary<string, string>();
            context.IsWarmupRequest = false;
            bool result = _instanceManager.StartAssignment(context);
            Assert.False(result);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, null);
        }

        private class TestEnvironmentEx : TestEnvironment
        {
            public override void SetEnvironmentVariable(string name, string value)
            {
                if (name == "throw")
                {
                    throw new InvalidOperationException("Kaboom!");
                }
                base.SetEnvironmentVariable(name, value);
            }
        }
    }
}
