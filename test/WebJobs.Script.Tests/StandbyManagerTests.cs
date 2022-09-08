// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class StandbyManagerTests
    {
        private Mock<IScriptHostManager> _mockHostManager;
        private Mock<IConfigurationRoot> _mockConfiguration;
        private Mock<IOptionsMonitor<ScriptApplicationHostOptions>> _mockOptionsMonitor;
        private Mock<IScriptWebHostEnvironment> _mockWebHostEnvironment;
        private Mock<IWebHostRpcWorkerChannelManager> _mockLanguageWorkerChannelManager;
        private TestEnvironment _testEnvironment;
        private string _testSettingName = "TestSetting";
        private string _testSettingValue = "TestSettingValue";
        private ILoggerProvider _testLoggerProvider;
        private ILoggerFactory _testLoggerFactory;
        private Mock<IApplicationLifetime> _mockApplicationLifetime;

        public StandbyManagerTests()
        {
            _mockHostManager = new Mock<IScriptHostManager>();
            _mockHostManager.Setup(m => m.State).Returns(ScriptHostState.Running);
            _mockConfiguration = new Mock<IConfigurationRoot>();
            _mockOptionsMonitor = new Mock<IOptionsMonitor<ScriptApplicationHostOptions>>();
            _mockWebHostEnvironment = new Mock<IScriptWebHostEnvironment>();
            _mockLanguageWorkerChannelManager = new Mock<IWebHostRpcWorkerChannelManager>();
            _testEnvironment = new TestEnvironment();
            _mockApplicationLifetime = new Mock<IApplicationLifetime>(MockBehavior.Strict);

            _testLoggerProvider = new TestLoggerProvider();
            _testLoggerFactory = new LoggerFactory();
            _testLoggerFactory.AddProvider(_testLoggerProvider);
        }

        [Fact]
        public async Task Specialize_ResetsConfiguration()
        {
            TestMetricsLogger metricsLogger = new TestMetricsLogger();
            var hostNameProvider = new HostNameProvider(_testEnvironment);
            var manager = new StandbyManager(_mockHostManager.Object, _mockLanguageWorkerChannelManager.Object, _mockConfiguration.Object, _mockWebHostEnvironment.Object, _testEnvironment, _mockOptionsMonitor.Object, NullLogger<StandbyManager>.Instance, hostNameProvider, _mockApplicationLifetime.Object, metricsLogger);

            await manager.SpecializeHostAsync();

            // Ensure metrics are generated
            Assert.True(AreExpectedMetricsGenerated(metricsLogger));

            _mockConfiguration.Verify(c => c.Reload());
        }

        [Fact]
        public async Task Specialize_ResetsHostNameProvider()
        {
            TestMetricsLogger metricsLogger = new TestMetricsLogger();
            _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName, "placeholder.azurewebsites.net");

            var hostNameProvider = new HostNameProvider(_testEnvironment);
            var manager = new StandbyManager(_mockHostManager.Object, _mockLanguageWorkerChannelManager.Object, _mockConfiguration.Object, _mockWebHostEnvironment.Object, _testEnvironment, _mockOptionsMonitor.Object, NullLogger<StandbyManager>.Instance, hostNameProvider, _mockApplicationLifetime.Object, metricsLogger);

            Assert.Equal("placeholder.azurewebsites.net", hostNameProvider.Value);

            await manager.SpecializeHostAsync();

            // Ensure metrics are generated
            Assert.True(AreExpectedMetricsGenerated(metricsLogger));

            _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName, "testapp.azurewebsites.net");
            Assert.Equal("testapp.azurewebsites.net", hostNameProvider.Value);

            _mockConfiguration.Verify(c => c.Reload());
        }

        [Fact]
        public async Task Specialize_ReloadsEnvironmentVariables()
        {
            TestMetricsLogger metricsLogger = new TestMetricsLogger();
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.JavaLanguageWorkerName);
            _mockLanguageWorkerChannelManager.Setup(m => m.SpecializeAsync()).Returns(async () =>
            {
                _testEnvironment.SetEnvironmentVariable(_testSettingName, _testSettingValue);
                await Task.Yield();
            });
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.JavaLanguageWorkerName);

            var hostNameProvider = new HostNameProvider(_testEnvironment);
            var manager = new StandbyManager(_mockHostManager.Object, _mockLanguageWorkerChannelManager.Object, _mockConfiguration.Object, _mockWebHostEnvironment.Object, _testEnvironment, _mockOptionsMonitor.Object, NullLogger<StandbyManager>.Instance, hostNameProvider, _mockApplicationLifetime.Object, metricsLogger);
            await manager.SpecializeHostAsync();

            // Ensure metrics are generated
            Assert.True(AreExpectedMetricsGenerated(metricsLogger));

            Assert.Equal(_testSettingValue, _testEnvironment.GetEnvironmentVariable(_testSettingName));
        }

        [Fact]
        public async Task Specialize_StandbyManagerInitialize_EmitsExpectedMetric()
        {
            TestMetricsLogger metricsLogger = new TestMetricsLogger();
            _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName, "placeholder.azurewebsites.net");

            var hostNameProvider = new HostNameProvider(_testEnvironment);
            var manager = new StandbyManager(_mockHostManager.Object, _mockLanguageWorkerChannelManager.Object, _mockConfiguration.Object, _mockWebHostEnvironment.Object, _testEnvironment, _mockOptionsMonitor.Object, NullLogger<StandbyManager>.Instance, hostNameProvider, _mockApplicationLifetime.Object, metricsLogger);
            await manager.InitializeAsync().ContinueWith(t => { }); // Ignore errors.

            // Ensure metric is generated
            Assert.True(metricsLogger.EventsBegan.Contains(MetricEventNames.SpecializationStandbyManagerInitialize) && metricsLogger.EventsEnded.Contains(MetricEventNames.SpecializationStandbyManagerInitialize));
        }

        [Fact]
        public async Task Specialize_StandbyManagerInitialize_SetsInitializedFromPlaceholderFlag()
        {
            TestMetricsLogger metricsLogger = new TestMetricsLogger();
            _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName, "placeholder.azurewebsites.net");

            var hostNameProvider = new HostNameProvider(_testEnvironment);
            var manager = new StandbyManager(_mockHostManager.Object, _mockLanguageWorkerChannelManager.Object, _mockConfiguration.Object, _mockWebHostEnvironment.Object, _testEnvironment, _mockOptionsMonitor.Object, NullLogger<StandbyManager>.Instance, hostNameProvider, _mockApplicationLifetime.Object, metricsLogger);
            await manager.InitializeAsync().ContinueWith(t => { }); // Ignore errors.

            // Ensure InitializedFromPlaceholder environment variable is set to true
            Assert.Equal(bool.TrueString, _testEnvironment.GetEnvironmentVariable(EnvironmentSettingNames.InitializedFromPlaceholder));
        }

        private bool AreExpectedMetricsGenerated(TestMetricsLogger metricsLogger)
        {
            return metricsLogger.EventsBegan.Contains(MetricEventNames.SpecializationSpecializeHost) && metricsLogger.EventsEnded.Contains(MetricEventNames.SpecializationSpecializeHost)
                    && metricsLogger.EventsBegan.Contains(MetricEventNames.SpecializationLanguageWorkerChannelManagerSpecialize) && metricsLogger.EventsEnded.Contains(MetricEventNames.SpecializationLanguageWorkerChannelManagerSpecialize)
                    && metricsLogger.EventsBegan.Contains(MetricEventNames.SpecializationRestartHost) && metricsLogger.EventsEnded.Contains(MetricEventNames.SpecializationRestartHost)
                    && metricsLogger.EventsBegan.Contains(MetricEventNames.SpecializationDelayUntilHostReady) && metricsLogger.EventsEnded.Contains(MetricEventNames.SpecializationDelayUntilHostReady);
        }
    }
}