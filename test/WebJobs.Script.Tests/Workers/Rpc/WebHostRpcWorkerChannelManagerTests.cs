// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Abstractions.Rpc;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class WebHostRpcWorkerChannelManagerTests
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
        private readonly Mock<IWorkerProcess> _rpcWorkerProcess;
        private readonly TestLogger _testLogger;

        private WebHostRpcWorkerChannelManager _rpcWorkerChannelManager;

        private string _scriptRootPath = @"c:\testing\FUNCTIONS-TEST";
        private IDictionary<string, string> _capabilities = new Dictionary<string, string>()
            {
                { "StandbyModeEnabled", "true" }
            };

        public WebHostRpcWorkerChannelManagerTests()
        {
            _eventManager = new ScriptEventManager();
            _rpcServer = new TestRpcServer();
            _loggerProvider = new TestLoggerProvider();
            _loggerFactory = new LoggerFactory();
            _testEnvironment = new TestEnvironment();
            _loggerFactory.AddProvider(_loggerProvider);
            _rpcWorkerProcess = new Mock<IWorkerProcess>();
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
            _rpcWorkerProcessFactory.Setup(m => m.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(_rpcWorkerProcess.Object);

            _testLogger = new TestLogger("WebHostLanguageWorkerChannelManagerTests");
            _rpcWorkerChannelFactory = new TestRpcWorkerChannelFactory(_eventManager, _testLogger, _scriptRootPath);
            _rpcWorkerChannelManager = new WebHostRpcWorkerChannelManager(_eventManager, _testEnvironment, _loggerFactory, _rpcWorkerChannelFactory, _optionsMonitor, new TestMetricsLogger());
        }

        [Fact]
        public async Task CreateChannels_Succeeds()
        {
            string language = RpcWorkerConstants.JavaLanguageWorkerName;
            IRpcWorkerChannel javaWorkerChannel = CreateTestChannel(language);
            var initializedChannel = await _rpcWorkerChannelManager.GetChannelAsync(language);
            IRpcWorkerChannel javaWorkerChannel2 = CreateTestChannel(RpcWorkerConstants.JavaLanguageWorkerName);

            Assert.NotNull(initializedChannel);
            Assert.Equal(javaWorkerChannel.Id, initializedChannel.Id);
            Assert.Equal(_rpcWorkerChannelManager.GetChannels(RpcWorkerConstants.JavaLanguageWorkerName).Count(), 2);
        }

        [Fact]
        public async Task ShutdownStandByChannels_Succeeds()
        {
            var testMetricsLogger = new TestMetricsLogger();
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.JavaLanguageWorkerName);
            _rpcWorkerChannelManager = new WebHostRpcWorkerChannelManager(_eventManager, _testEnvironment, _loggerFactory, _rpcWorkerChannelFactory, _optionsMonitor, testMetricsLogger);

            IRpcWorkerChannel javaWorkerChannel = CreateTestChannel(RpcWorkerConstants.JavaLanguageWorkerName);

            IRpcWorkerChannel nodeWorkerChannel = CreateTestChannel(RpcWorkerConstants.NodeLanguageWorkerName);

            _rpcWorkerChannelManager.ScheduleShutdownStandbyChannels();
            Assert.True(AreRequiredMetricsEmitted(testMetricsLogger));
            var initializedChannel = await _rpcWorkerChannelManager.GetChannelAsync(RpcWorkerConstants.NodeLanguageWorkerName);
            Assert.Null(initializedChannel);
            Assert.Null(_rpcWorkerChannelManager.GetChannels(RpcWorkerConstants.NodeLanguageWorkerName));
            initializedChannel = await _rpcWorkerChannelManager.GetChannelAsync(RpcWorkerConstants.JavaLanguageWorkerName);
            Assert.NotNull(initializedChannel);
            Assert.Equal(javaWorkerChannel.Id, initializedChannel.Id);
        }

        [Fact]
        public async Task ShutdownStandByChannels_WorkerRuntinmeDotNet_Succeeds()
        {
            var testMetricsLogger = new TestMetricsLogger();
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.DotNetLanguageWorkerName);
            _rpcWorkerChannelManager = new WebHostRpcWorkerChannelManager(_eventManager, _testEnvironment, _loggerFactory, _rpcWorkerChannelFactory, _optionsMonitor, testMetricsLogger);

            IRpcWorkerChannel javaWorkerChannel = CreateTestChannel(RpcWorkerConstants.JavaLanguageWorkerName);
            IRpcWorkerChannel nodeWorkerChannel = CreateTestChannel(RpcWorkerConstants.NodeLanguageWorkerName);

            _rpcWorkerChannelManager.ScheduleShutdownStandbyChannels();
            Assert.True(AreRequiredMetricsEmitted(testMetricsLogger));
            IRpcWorkerChannel initializedChannel = await _rpcWorkerChannelManager.GetChannelAsync(RpcWorkerConstants.NodeLanguageWorkerName);
            Assert.Null(initializedChannel);
            initializedChannel = await _rpcWorkerChannelManager.GetChannelAsync(RpcWorkerConstants.JavaLanguageWorkerName);
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
            await _rpcWorkerChannelManager.ShutdownChannelsAsync();
            Assert.Null(_rpcWorkerChannelManager.GetChannels(RpcWorkerConstants.JavaLanguageWorkerName));
            Assert.Null(_rpcWorkerChannelManager.GetChannels(RpcWorkerConstants.NodeLanguageWorkerName));

            // Verify disposed
            IRpcWorkerChannel initializedChannel = await _rpcWorkerChannelManager.GetChannelAsync(RpcWorkerConstants.NodeLanguageWorkerName);
            Assert.Null(initializedChannel);
            initializedChannel = await _rpcWorkerChannelManager.GetChannelAsync(RpcWorkerConstants.JavaLanguageWorkerName);
            Assert.Null(initializedChannel);
        }

        [Fact]
        public async Task ShutdownStandyChannels_WorkerRuntime_Node_Set()
        {
            var testMetricsLogger = new TestMetricsLogger();
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.NodeLanguageWorkerName);
            _rpcWorkerChannelManager = new WebHostRpcWorkerChannelManager(_eventManager, _testEnvironment, _loggerFactory, _rpcWorkerChannelFactory, _optionsMonitor, testMetricsLogger);

            IRpcWorkerChannel javaWorkerChannel = CreateTestChannel(RpcWorkerConstants.JavaLanguageWorkerName);

            _rpcWorkerChannelManager.ScheduleShutdownStandbyChannels();
            Assert.True(AreRequiredMetricsEmitted(testMetricsLogger));
            var initializedChannel = await _rpcWorkerChannelManager.GetChannelAsync(RpcWorkerConstants.JavaLanguageWorkerName);
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

            _rpcWorkerChannelManager = new WebHostRpcWorkerChannelManager(_eventManager, _testEnvironment, _loggerFactory, _rpcWorkerChannelFactory, _optionsMonitor, testMetricsLogger);

            IRpcWorkerChannel nodeWorkerChannel = CreateTestChannel(RpcWorkerConstants.NodeLanguageWorkerName);

            await _rpcWorkerChannelManager.SpecializeAsync();
            Assert.True(testMetricsLogger.EventsBegan.Contains(MetricEventNames.SpecializationScheduleShutdownStandbyChannels)
                && testMetricsLogger.EventsEnded.Contains(MetricEventNames.SpecializationScheduleShutdownStandbyChannels));

            // Verify logs
            var traces = _testLogger.GetLogMessages();
            var functionLoadLogs = traces.Where(m => string.Equals(m.FormattedMessage, "SendFunctionEnvironmentReloadRequest called"));
            Assert.True(functionLoadLogs.Count() == 1);

            // Verify channel
            var initializedChannel = await _rpcWorkerChannelManager.GetChannelAsync(RpcWorkerConstants.NodeLanguageWorkerName);
            Assert.Equal(nodeWorkerChannel, initializedChannel);
        }

        [Fact]
        public async Task SpecializeAsync_Java_KeepsProcessAlive()
        {
            var testMetricsLogger = new TestMetricsLogger();
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.JavaLanguageWorkerName);
            _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteZipDeployment, "0");

            _rpcWorkerChannelManager = new WebHostRpcWorkerChannelManager(_eventManager, _testEnvironment, _loggerFactory, _rpcWorkerChannelFactory, _optionsMonitor, testMetricsLogger);

            IRpcWorkerChannel javaWorkerChannel = CreateTestChannel(RpcWorkerConstants.JavaLanguageWorkerName);

            await _rpcWorkerChannelManager.SpecializeAsync();

            Assert.True(testMetricsLogger.EventsBegan.Contains(MetricEventNames.SpecializationScheduleShutdownStandbyChannels)
                && testMetricsLogger.EventsEnded.Contains(MetricEventNames.SpecializationScheduleShutdownStandbyChannels));

            // Verify logs
            var traces = _testLogger.GetLogMessages();
            var functionLoadLogs = traces.Where(m => string.Equals(m.FormattedMessage, "SendFunctionEnvironmentReloadRequest called"));
            Assert.True(functionLoadLogs.Count() == 1);

            // Verify channel
            var initializedChannel = await _rpcWorkerChannelManager.GetChannelAsync(RpcWorkerConstants.JavaLanguageWorkerName);
            Assert.Equal(javaWorkerChannel, initializedChannel);
        }

        [Fact]
        public async Task SpecializeAsync_Java_ReadOnly_KeepsProcessAlive()
        {
            var testMetricsLogger = new TestMetricsLogger();
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.JavaLanguageWorkerName);
            _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteZipDeployment, "1");

            _rpcWorkerChannelManager = new WebHostRpcWorkerChannelManager(_eventManager, _testEnvironment, _loggerFactory, _rpcWorkerChannelFactory, _optionsMonitor, testMetricsLogger);

            IRpcWorkerChannel javaWorkerChannel = CreateTestChannel(RpcWorkerConstants.JavaLanguageWorkerName);

            await _rpcWorkerChannelManager.SpecializeAsync();

            Assert.True(testMetricsLogger.EventsBegan.Contains(MetricEventNames.SpecializationScheduleShutdownStandbyChannels)
                && testMetricsLogger.EventsEnded.Contains(MetricEventNames.SpecializationScheduleShutdownStandbyChannels));

            // Verify logs
            var traces = _testLogger.GetLogMessages();
            var functionLoadLogs = traces.Where(m => string.Equals(m.FormattedMessage, "SendFunctionEnvironmentReloadRequest called"));
            Assert.True(functionLoadLogs.Count() == 1);

            // Verify channel
            var initializedChannel = await _rpcWorkerChannelManager.GetChannelAsync(RpcWorkerConstants.JavaLanguageWorkerName);
            Assert.Equal(javaWorkerChannel, initializedChannel);
        }

        [Fact]
        public async Task SpecializeAsync_Node_NotReadOnly_KillsProcess()
        {
            var testMetricsLogger = new TestMetricsLogger();
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.NodeLanguageWorkerName);
            // This is an invalid setting configuration, but just to show that run from zip is NOT set
            _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteZipDeployment, "0");

            _rpcWorkerChannelManager = new WebHostRpcWorkerChannelManager(_eventManager, _testEnvironment, _loggerFactory, _rpcWorkerChannelFactory, _optionsMonitor, testMetricsLogger);

            IRpcWorkerChannel nodeWorkerChannel = CreateTestChannel(RpcWorkerConstants.NodeLanguageWorkerName);

            await _rpcWorkerChannelManager.SpecializeAsync();

            // Verify logs
            var traces = _testLogger.GetLogMessages();
            Assert.True(traces.Count() == 0);

            // Verify channel
            var initializedChannel = await _rpcWorkerChannelManager.GetChannelAsync(RpcWorkerConstants.NodeLanguageWorkerName);
            Assert.Null(initializedChannel);
        }

        [Fact]
        public async Task ShutdownStandbyChannels_WorkerRuntime_Not_Set()
        {
            IRpcWorkerChannel javaWorkerChannel = CreateTestChannel(RpcWorkerConstants.JavaLanguageWorkerName);

            await _rpcWorkerChannelManager.ShutdownChannelsAsync();

            IRpcWorkerChannel initializedChannel = await _rpcWorkerChannelManager.GetChannelAsync(RpcWorkerConstants.JavaLanguageWorkerName);
            Assert.Null(initializedChannel);
        }

        [Fact]
        public async Task ShutdownChannelsIfExist_Succeeds()
        {
            IRpcWorkerChannel javaWorkerChannel1 = CreateTestChannel(RpcWorkerConstants.JavaLanguageWorkerName);
            IRpcWorkerChannel javaWorkerChannel2 = CreateTestChannel(RpcWorkerConstants.JavaLanguageWorkerName);

            await _rpcWorkerChannelManager.ShutdownChannelIfExistsAsync(RpcWorkerConstants.JavaLanguageWorkerName, javaWorkerChannel1.Id);
            await _rpcWorkerChannelManager.ShutdownChannelIfExistsAsync(RpcWorkerConstants.JavaLanguageWorkerName, javaWorkerChannel2.Id);

            Assert.Null(_rpcWorkerChannelManager.GetChannels(RpcWorkerConstants.JavaLanguageWorkerName));

            var initializedChannel = await _rpcWorkerChannelManager.GetChannelAsync(RpcWorkerConstants.JavaLanguageWorkerName);
            Assert.Null(initializedChannel);
        }

        [Fact]
        public async Task InitializeLanguageWorkerChannel_ThrowsOnProcessStartup()
        {
            var rpcWorkerChannelFactory = new TestRpcWorkerChannelFactory(_eventManager, null, _scriptRootPath, throwOnProcessStartUp: true);
            var rpcWorkerChannelManager = new WebHostRpcWorkerChannelManager(_eventManager, _testEnvironment, _loggerFactory, rpcWorkerChannelFactory, _optionsMonitor, new TestMetricsLogger());
            var rpcWorkerChannel = await rpcWorkerChannelManager.InitializeLanguageWorkerChannel("test", _scriptRootPath);
            var ex = await Assert.ThrowsAsync<AggregateException>(async () => await rpcWorkerChannelManager.GetChannelAsync("test"));
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
            var testChannel = _rpcWorkerChannelFactory.Create(_scriptRootPath, language, null, 0);
            _rpcWorkerChannelManager.AddOrUpdateWorkerChannels(language, testChannel);
            _rpcWorkerChannelManager.SetInitializedWorkerChannel(language, testChannel);
            return testChannel;
        }
    }
}