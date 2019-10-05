// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Rpc
{
    public class WebHostLanguageWorkerChannelManagerTests
    {
        private readonly IScriptEventManager _eventManager;
        private readonly IEnvironment _testEnvironment;
        private readonly IRpcServer _rpcServer;
        private readonly TestLoggerProvider _loggerProvider;
        private readonly ILoggerFactory _loggerFactory;
        private readonly LanguageWorkerOptions _languageWorkerOptions;
        private readonly Mock<IRpcWorkerProcessFactory> _rpcWorkerProcessFactory;
        private readonly IRpcWorkerChannelFactory _rpcWorkerChannelFactory;
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _optionsMonitor;
        private readonly Mock<IWorkerProcess> _languageWorkerProcess;
        private readonly TestLogger _testLogger;

        private WebHostRpcWorkerChannelManager _languageWorkerChannelManager;

        private string _scriptRootPath = @"c:\testing\FUNCTIONS-TEST";
        private IDictionary<string, string> _capabilities = new Dictionary<string, string>()
            {
                { "StandbyModeEnabled", "true" }
            };

        public WebHostLanguageWorkerChannelManagerTests()
        {
            _eventManager = new ScriptEventManager();
            _rpcServer = new TestRpcServer();
            _loggerProvider = new TestLoggerProvider();
            _loggerFactory = new LoggerFactory();
            _testEnvironment = new TestEnvironment();
            _loggerFactory.AddProvider(_loggerProvider);
            _languageWorkerProcess = new Mock<IWorkerProcess>();
            _languageWorkerOptions = new LanguageWorkerOptions
            {
                WorkerConfigs = TestHelpers.GetTestWorkerConfigs()
            };
            var applicationHostOptions = new ScriptApplicationHostOptions
            {
                IsSelfHost = true,
                ScriptPath = @"c:\testing\FUNCTIONS-TEST\test$#"
            };
            _optionsMonitor = TestHelpers.CreateOptionsMonitor(applicationHostOptions);

            _rpcWorkerProcessFactory = new Mock<IRpcWorkerProcessFactory>();
            _rpcWorkerProcessFactory.Setup(m => m.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(_languageWorkerProcess.Object);

            _testLogger = new TestLogger("WebHostLanguageWorkerChannelManagerTests");
            _rpcWorkerChannelFactory = new TestLanguageWorkerChannelFactory(_eventManager, _testLogger, _scriptRootPath);
            _languageWorkerChannelManager = new WebHostRpcWorkerChannelManager(_eventManager, _testEnvironment, _loggerFactory, _rpcWorkerChannelFactory, _optionsMonitor, new TestMetricsLogger());
        }

        [Fact]
        public async Task CreateChannels_Succeeds()
        {
            string language = RpcWorkerConstants.JavaLanguageWorkerName;
            IRpcWorkerChannel javaWorkerChannel = CreateTestChannel(language);
            var initializedChannel = await _languageWorkerChannelManager.GetChannelAsync(language);
            IRpcWorkerChannel javaWorkerChannel2 = CreateTestChannel(RpcWorkerConstants.JavaLanguageWorkerName);

            Assert.NotNull(initializedChannel);
            Assert.Equal(javaWorkerChannel.Id, initializedChannel.Id);
            Assert.Equal(_languageWorkerChannelManager.GetChannels(RpcWorkerConstants.JavaLanguageWorkerName).Count(), 2);
        }

        [Fact]
        public async Task ShutdownStandByChannels_Succeeds()
        {
            var testMetricsLogger = new TestMetricsLogger();
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.JavaLanguageWorkerName);
            _languageWorkerChannelManager = new WebHostRpcWorkerChannelManager(_eventManager, _testEnvironment, _loggerFactory, _rpcWorkerChannelFactory, _optionsMonitor, testMetricsLogger);

            IRpcWorkerChannel javaWorkerChannel = CreateTestChannel(RpcWorkerConstants.JavaLanguageWorkerName);

            IRpcWorkerChannel nodeWorkerChannel = CreateTestChannel(RpcWorkerConstants.NodeLanguageWorkerName);

            _languageWorkerChannelManager.ScheduleShutdownStandbyChannels();
            Assert.True(AreRequiredMetricsEmitted(testMetricsLogger));
            var initializedChannel = await _languageWorkerChannelManager.GetChannelAsync(RpcWorkerConstants.NodeLanguageWorkerName);
            Assert.Null(initializedChannel);
            Assert.Null(_languageWorkerChannelManager.GetChannels(RpcWorkerConstants.NodeLanguageWorkerName));
            initializedChannel = await _languageWorkerChannelManager.GetChannelAsync(RpcWorkerConstants.JavaLanguageWorkerName);
            Assert.NotNull(initializedChannel);
            Assert.Equal(javaWorkerChannel.Id, initializedChannel.Id);
        }

        [Fact]
        public async Task ShutdownStandByChannels_WorkerRuntinmeDotNet_Succeeds()
        {
            var testMetricsLogger = new TestMetricsLogger();
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.DotNetLanguageWorkerName);
            _languageWorkerChannelManager = new WebHostRpcWorkerChannelManager(_eventManager, _testEnvironment, _loggerFactory, _rpcWorkerChannelFactory, _optionsMonitor, testMetricsLogger);

            IRpcWorkerChannel javaWorkerChannel = CreateTestChannel(RpcWorkerConstants.JavaLanguageWorkerName);
            IRpcWorkerChannel nodeWorkerChannel = CreateTestChannel(RpcWorkerConstants.NodeLanguageWorkerName);

            _languageWorkerChannelManager.ScheduleShutdownStandbyChannels();
            Assert.True(AreRequiredMetricsEmitted(testMetricsLogger));
            IRpcWorkerChannel initializedChannel = await _languageWorkerChannelManager.GetChannelAsync(RpcWorkerConstants.NodeLanguageWorkerName);
            Assert.Null(initializedChannel);
            initializedChannel = await _languageWorkerChannelManager.GetChannelAsync(RpcWorkerConstants.JavaLanguageWorkerName);
            Assert.Null(initializedChannel);
        }

        [Fact]
        public async Task ShutdownChannels_Succeeds()
        {
            string javaWorkerId = Guid.NewGuid().ToString();
            IRpcWorkerChannel javaWorkerChannel = CreateTestChannel(RpcWorkerConstants.JavaLanguageWorkerName);

            string nodeWorkerId = Guid.NewGuid().ToString();
            IRpcWorkerChannel nodeWorkerChannel = CreateTestChannel(RpcWorkerConstants.NodeLanguageWorkerName);

            // Shutdown
            await _languageWorkerChannelManager.ShutdownChannelsAsync();
            Assert.Null(_languageWorkerChannelManager.GetChannels(RpcWorkerConstants.JavaLanguageWorkerName));
            Assert.Null(_languageWorkerChannelManager.GetChannels(RpcWorkerConstants.NodeLanguageWorkerName));

            // Verify disposed
            IRpcWorkerChannel initializedChannel = await _languageWorkerChannelManager.GetChannelAsync(RpcWorkerConstants.NodeLanguageWorkerName);
            Assert.Null(initializedChannel);
            initializedChannel = await _languageWorkerChannelManager.GetChannelAsync(RpcWorkerConstants.JavaLanguageWorkerName);
            Assert.Null(initializedChannel);
        }

        [Fact]
        public async Task ShutdownStandyChannels_WorkerRuntime_Node_Set()
        {
            var testMetricsLogger = new TestMetricsLogger();
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.NodeLanguageWorkerName);
            _languageWorkerChannelManager = new WebHostRpcWorkerChannelManager(_eventManager, _testEnvironment, _loggerFactory, _rpcWorkerChannelFactory, _optionsMonitor, testMetricsLogger);

            IRpcWorkerChannel javaWorkerChannel = CreateTestChannel(RpcWorkerConstants.JavaLanguageWorkerName);

            _languageWorkerChannelManager.ScheduleShutdownStandbyChannels();
            Assert.True(AreRequiredMetricsEmitted(testMetricsLogger));
            var initializedChannel = await _languageWorkerChannelManager.GetChannelAsync(RpcWorkerConstants.JavaLanguageWorkerName);
            Assert.Null(initializedChannel);
        }

        [Theory]
        [InlineData("nOde")]
        [InlineData("Node")]
        public async Task SpecializeAsync_Node_ReadOnly_KeepsProcessAlive(string runtime)
        {
            var testMetricsLogger = new TestMetricsLogger();
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, runtime);
            _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteZipDeployment, "1");

            _languageWorkerChannelManager = new WebHostRpcWorkerChannelManager(_eventManager, _testEnvironment, _loggerFactory, _rpcWorkerChannelFactory, _optionsMonitor, testMetricsLogger);

            IRpcWorkerChannel nodeWorkerChannel = CreateTestChannel(RpcWorkerConstants.NodeLanguageWorkerName);

            await _languageWorkerChannelManager.SpecializeAsync();
            Assert.True(testMetricsLogger.EventsBegan.Contains(MetricEventNames.SpecializationScheduleShutdownStandbyChannels)
                && testMetricsLogger.EventsEnded.Contains(MetricEventNames.SpecializationScheduleShutdownStandbyChannels));

            // Verify logs
            var traces = _testLogger.GetLogMessages();
            var functionLoadLogs = traces.Where(m => string.Equals(m.FormattedMessage, "SendFunctionEnvironmentReloadRequest called"));
            Assert.True(functionLoadLogs.Count() == 1);

            // Verify channel
            var initializedChannel = await _languageWorkerChannelManager.GetChannelAsync(RpcWorkerConstants.NodeLanguageWorkerName);
            Assert.Equal(nodeWorkerChannel, initializedChannel);
        }

        [Fact]
        public async Task SpecializeAsync_Java_KeepsProcessAlive()
        {
            var testMetricsLogger = new TestMetricsLogger();
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.JavaLanguageWorkerName);
            _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteZipDeployment, "0");

            _languageWorkerChannelManager = new WebHostRpcWorkerChannelManager(_eventManager, _testEnvironment, _loggerFactory, _rpcWorkerChannelFactory, _optionsMonitor, testMetricsLogger);

            IRpcWorkerChannel javaWorkerChannel = CreateTestChannel(RpcWorkerConstants.JavaLanguageWorkerName);

            await _languageWorkerChannelManager.SpecializeAsync();

            Assert.True(testMetricsLogger.EventsBegan.Contains(MetricEventNames.SpecializationScheduleShutdownStandbyChannels)
                && testMetricsLogger.EventsEnded.Contains(MetricEventNames.SpecializationScheduleShutdownStandbyChannels));

            // Verify logs
            var traces = _testLogger.GetLogMessages();
            var functionLoadLogs = traces.Where(m => string.Equals(m.FormattedMessage, "SendFunctionEnvironmentReloadRequest called"));
            Assert.True(functionLoadLogs.Count() == 1);

            // Verify channel
            var initializedChannel = await _languageWorkerChannelManager.GetChannelAsync(RpcWorkerConstants.JavaLanguageWorkerName);
            Assert.Equal(javaWorkerChannel, initializedChannel);
        }

        [Fact]
        public async Task SpecializeAsync_Java_ReadOnly_KeepsProcessAlive()
        {
            var testMetricsLogger = new TestMetricsLogger();
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.JavaLanguageWorkerName);
            _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteZipDeployment, "1");

            _languageWorkerChannelManager = new WebHostRpcWorkerChannelManager(_eventManager, _testEnvironment, _loggerFactory, _rpcWorkerChannelFactory, _optionsMonitor, testMetricsLogger);

            IRpcWorkerChannel javaWorkerChannel = CreateTestChannel(RpcWorkerConstants.JavaLanguageWorkerName);

            await _languageWorkerChannelManager.SpecializeAsync();

            Assert.True(testMetricsLogger.EventsBegan.Contains(MetricEventNames.SpecializationScheduleShutdownStandbyChannels)
                && testMetricsLogger.EventsEnded.Contains(MetricEventNames.SpecializationScheduleShutdownStandbyChannels));

            // Verify logs
            var traces = _testLogger.GetLogMessages();
            var functionLoadLogs = traces.Where(m => string.Equals(m.FormattedMessage, "SendFunctionEnvironmentReloadRequest called"));
            Assert.True(functionLoadLogs.Count() == 1);

            // Verify channel
            var initializedChannel = await _languageWorkerChannelManager.GetChannelAsync(RpcWorkerConstants.JavaLanguageWorkerName);
            Assert.Equal(javaWorkerChannel, initializedChannel);
        }

        [Fact]
        public async Task SpecializeAsync_Node_NotReadOnly_KillsProcess()
        {
            var testMetricsLogger = new TestMetricsLogger();
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.NodeLanguageWorkerName);
            // This is an invalid setting configuration, but just to show that run from zip is NOT set
            _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteZipDeployment, "0");

            _languageWorkerChannelManager = new WebHostRpcWorkerChannelManager(_eventManager, _testEnvironment, _loggerFactory, _rpcWorkerChannelFactory, _optionsMonitor, testMetricsLogger);

            IRpcWorkerChannel nodeWorkerChannel = CreateTestChannel(RpcWorkerConstants.NodeLanguageWorkerName);

            await _languageWorkerChannelManager.SpecializeAsync();

            // Verify logs
            var traces = _testLogger.GetLogMessages();
            Assert.True(traces.Count() == 0);

            // Verify channel
            var initializedChannel = await _languageWorkerChannelManager.GetChannelAsync(RpcWorkerConstants.NodeLanguageWorkerName);
            Assert.Null(initializedChannel);
        }

        [Fact]
        public async Task ShutdownStandbyChannels_WorkerRuntime_Not_Set()
        {
            IRpcWorkerChannel javaWorkerChannel = CreateTestChannel(RpcWorkerConstants.JavaLanguageWorkerName);

            await _languageWorkerChannelManager.ShutdownChannelsAsync();

            IRpcWorkerChannel initializedChannel = await _languageWorkerChannelManager.GetChannelAsync(RpcWorkerConstants.JavaLanguageWorkerName);
            Assert.Null(initializedChannel);
        }

        [Fact]
        public async Task ShutdownChannelsIfExist_Succeeds()
        {
            IRpcWorkerChannel javaWorkerChannel1 = CreateTestChannel(RpcWorkerConstants.JavaLanguageWorkerName);
            IRpcWorkerChannel javaWorkerChannel2 = CreateTestChannel(RpcWorkerConstants.JavaLanguageWorkerName);

            await _languageWorkerChannelManager.ShutdownChannelIfExistsAsync(RpcWorkerConstants.JavaLanguageWorkerName, javaWorkerChannel1.Id);
            await _languageWorkerChannelManager.ShutdownChannelIfExistsAsync(RpcWorkerConstants.JavaLanguageWorkerName, javaWorkerChannel2.Id);

            Assert.Null(_languageWorkerChannelManager.GetChannels(RpcWorkerConstants.JavaLanguageWorkerName));

            var initializedChannel = await _languageWorkerChannelManager.GetChannelAsync(RpcWorkerConstants.JavaLanguageWorkerName);
            Assert.Null(initializedChannel);
        }

        [Fact]
        public async Task InitializeLanguageWorkerChannel_ThrowsOnProcessStartup()
        {
            var languageWorkerChannelFactory = new TestLanguageWorkerChannelFactory(_eventManager, null, _scriptRootPath, throwOnProcessStartUp: true);
            var languageWorkerChannelManager = new WebHostRpcWorkerChannelManager(_eventManager, _testEnvironment, _loggerFactory, languageWorkerChannelFactory, _optionsMonitor, new TestMetricsLogger());
            var languageWorkerChannel = await languageWorkerChannelManager.InitializeLanguageWorkerChannel("test", _scriptRootPath);
            var ex = await Assert.ThrowsAsync<AggregateException>(async () => await languageWorkerChannelManager.GetChannelAsync("test"));
            Assert.Contains("Process startup failed", ex.InnerException.Message);
        }

        private bool AreRequiredMetricsEmitted(TestMetricsLogger metricsLogger)
        {
            bool hasBegun = false;
            bool hasEnded = false;
            foreach (string begin in metricsLogger.EventsBegan)
            {
                if (begin.Contains(MetricEventNames.SpecializationShutdownStandbyChannels.Substring(0, MetricEventNames.SpecializationShutdownStandbyChannels.IndexOf('{'))))
                {
                    hasBegun = true;
                    break;
                }
            }
            foreach (string end in metricsLogger.EventsEnded)
            {
                if (end.Contains(MetricEventNames.SpecializationShutdownStandbyChannels.Substring(0, MetricEventNames.SpecializationShutdownStandbyChannels.IndexOf('{'))))
                {
                    hasEnded = true;
                    break;
                }
            }
            return hasBegun && hasEnded;
        }

        private IRpcWorkerChannel CreateTestChannel(string language)
        {
            var testChannel = _rpcWorkerChannelFactory.Create(_scriptRootPath, language, null, 0, null);
            _languageWorkerChannelManager.AddOrUpdateWorkerChannels(language, testChannel);
            _languageWorkerChannelManager.SetInitializedWorkerChannel(language, testChannel);
            return testChannel;
        }
    }
}