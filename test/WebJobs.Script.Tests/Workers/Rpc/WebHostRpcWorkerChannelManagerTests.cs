// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Configuration;
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
        private readonly IOptionsMonitor<LanguageWorkerOptions> _workerOptionsMonitor;
        private readonly Mock<IWorkerProcess> _rpcWorkerProcess;
        private readonly TestLogger _testLogger;
        private readonly IConfiguration _emptyConfig;
        private readonly TestSystemRuntimeInformation _testSysRuntimeInfo = new TestSystemRuntimeInformation();

        private WebHostRpcWorkerChannelManager _rpcWorkerChannelManager;
        private IWorkerProfileManager _workerProfileManager;

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

            var workerProfileLogger = new TestLogger<WorkerProfileManager>();
            _workerProfileManager = new WorkerProfileManager(workerProfileLogger, _testEnvironment);

            var applicationHostOptions = new ScriptApplicationHostOptions
            {
                IsSelfHost = true,
                ScriptPath = @"c:\testing\FUNCTIONS-TEST\test$#"
            };

            _optionsMonitor = TestHelpers.CreateOptionsMonitor(applicationHostOptions);

            _workerOptionsMonitor = TestHelpers.CreateOptionsMonitor(_languageWorkerOptions);

            _rpcWorkerProcessFactory = new Mock<IRpcWorkerProcessFactory>();
            _rpcWorkerProcessFactory.Setup(m => m.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<RpcWorkerConfig>())).Returns(_rpcWorkerProcess.Object);

            _testLogger = new TestLogger("WebHostLanguageWorkerChannelManagerTests");
            _rpcWorkerChannelFactory = new TestRpcWorkerChannelFactory(_eventManager, _testLogger, _scriptRootPath);
            _emptyConfig = new ConfigurationBuilder().Build();
            _rpcWorkerChannelManager = CreateChannelManager();
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

            // test case insensitivity
            Assert.Equal(_rpcWorkerChannelManager.GetChannels("Java").Count(), 2);
        }

        [Fact]
        public async Task ShutdownStandByChannels_Succeeds()
        {
            var testMetricsLogger = new TestMetricsLogger();
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.JavaLanguageWorkerName);
            _rpcWorkerChannelManager = CreateChannelManager(metrics: testMetricsLogger);

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
            _rpcWorkerChannelManager = CreateChannelManager(metrics: testMetricsLogger);

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
            _rpcWorkerChannelManager = CreateChannelManager(metrics: testMetricsLogger);

            IRpcWorkerChannel javaWorkerChannel = CreateTestChannel(RpcWorkerConstants.JavaLanguageWorkerName);

            _rpcWorkerChannelManager.ScheduleShutdownStandbyChannels();
            Assert.True(AreRequiredMetricsEmitted(testMetricsLogger));
            var initializedChannel = await _rpcWorkerChannelManager.GetChannelAsync(RpcWorkerConstants.JavaLanguageWorkerName);
            Assert.Null(initializedChannel);
        }

        [Theory]
        [InlineData("nOde", RpcWorkerConstants.NodeLanguageWorkerName)]
        [InlineData("Node", RpcWorkerConstants.NodeLanguageWorkerName)]
        [InlineData("PowerShell", RpcWorkerConstants.PowerShellLanguageWorkerName)]
        [InlineData("pOwerShell", RpcWorkerConstants.PowerShellLanguageWorkerName)]
        [InlineData("python", RpcWorkerConstants.PythonLanguageWorkerName)]
        [InlineData("pythoN", RpcWorkerConstants.PythonLanguageWorkerName)]
        public async Task SpecializeAsync_ReadOnly_KeepsProcessAlive(string runtime, string languageWorkerName)
        {
            var testMetricsLogger = new TestMetricsLogger();
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, runtime);
            _optionsMonitor.CurrentValue.IsFileSystemReadOnly = true;

            _rpcWorkerChannelManager = CreateChannelManager(metrics: testMetricsLogger);

            var workerConfigs = _workerOptionsMonitor.CurrentValue.WorkerConfigs;
            workerConfigs.Add(new RpcWorkerConfig
            {
                Description = TestHelpers.GetTestWorkerDescription("powershell", ".ps1", workerIndexing: true),
                CountOptions = new WorkerProcessCountOptions()
            });
            workerConfigs.Add(new RpcWorkerConfig
            {
                Description = TestHelpers.GetTestWorkerDescription("python", ".py", workerIndexing: true),
                CountOptions = new WorkerProcessCountOptions()
            });

            IRpcWorkerChannel workerChannel = CreateTestChannel(languageWorkerName);

            await _rpcWorkerChannelManager.SpecializeAsync();
            // Wait for debounce task to start
            await TestHelpers.Await(() =>
            {
                return testMetricsLogger.EventsBegan.Contains(MetricEventNames.SpecializationScheduleShutdownStandbyChannels)
                && testMetricsLogger.EventsEnded.Contains(MetricEventNames.SpecializationScheduleShutdownStandbyChannels);
            }, pollingInterval: 500);

            // Verify logs
            var traces = _testLogger.GetLogMessages();
            var functionLoadLogs = traces.Where(m => string.Equals(m.FormattedMessage, "SendFunctionEnvironmentReloadRequest called"));
            Assert.True(functionLoadLogs.Count() == 1);

            // Verify channel
            var initializedChannel = await _rpcWorkerChannelManager.GetChannelAsync(languageWorkerName);
            Assert.Equal(workerChannel, initializedChannel);
        }

        [Fact]
        public async Task SpecializeAsync_Java_KeepsProcessAlive()
        {
            var testMetricsLogger = new TestMetricsLogger();
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.JavaLanguageWorkerName);
            _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteZipDeployment, "0");

            _rpcWorkerChannelManager = CreateChannelManager(metrics: testMetricsLogger);

            IRpcWorkerChannel javaWorkerChannel = CreateTestChannel(RpcWorkerConstants.JavaLanguageWorkerName);

            await _rpcWorkerChannelManager.SpecializeAsync();

            // Wait for debounce task to start
            await TestHelpers.Await(() =>
            {
                return testMetricsLogger.EventsBegan.Contains(MetricEventNames.SpecializationScheduleShutdownStandbyChannels)
                && testMetricsLogger.EventsEnded.Contains(MetricEventNames.SpecializationScheduleShutdownStandbyChannels);
            }, pollingInterval: 500);

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

            _rpcWorkerChannelManager = CreateChannelManager(metrics: testMetricsLogger);

            IRpcWorkerChannel javaWorkerChannel = CreateTestChannel(RpcWorkerConstants.JavaLanguageWorkerName);

            await _rpcWorkerChannelManager.SpecializeAsync();

            // Wait for debouce task to start
            await TestHelpers.Await(() =>
            {
                return testMetricsLogger.EventsBegan.Contains(MetricEventNames.SpecializationScheduleShutdownStandbyChannels)
                && testMetricsLogger.EventsEnded.Contains(MetricEventNames.SpecializationScheduleShutdownStandbyChannels);
            }, pollingInterval: 500);

            // Verify logs
            var traces = _testLogger.GetLogMessages();
            var functionLoadLogs = traces.Where(m => string.Equals(m.FormattedMessage, "SendFunctionEnvironmentReloadRequest called"));
            Assert.True(functionLoadLogs.Count() == 1);

            // Verify channel
            var initializedChannel = await _rpcWorkerChannelManager.GetChannelAsync(RpcWorkerConstants.JavaLanguageWorkerName);
            Assert.Equal(javaWorkerChannel, initializedChannel);
        }

        [Theory]
        [InlineData(RpcWorkerConstants.NodeLanguageWorkerName)]
        [InlineData(RpcWorkerConstants.PowerShellLanguageWorkerName)]
        [InlineData(RpcWorkerConstants.PythonLanguageWorkerName)]
        public async Task SpecializeAsync_NotReadOnly_KillsProcess(string languageWorkerName)
        {
            var testMetricsLogger = new TestMetricsLogger();
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, languageWorkerName);
            // This is an invalid setting configuration, but just to show that run from zip is NOT set
            _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteZipDeployment, "0");

            _rpcWorkerChannelManager = CreateChannelManager(metrics: testMetricsLogger);

            IRpcWorkerChannel workerChannel = CreateTestChannel(languageWorkerName);

            await _rpcWorkerChannelManager.SpecializeAsync();

            // Verify logs
            var traces = _testLogger.GetLogMessages();
            Assert.True(traces.Count() == 0);

            // Verify channel
            var initializedChannel = await _rpcWorkerChannelManager.GetChannelAsync(languageWorkerName);
            Assert.Null(initializedChannel);
        }

        [Theory]
        [InlineData(RpcWorkerConstants.NodeLanguageWorkerName, "--inspect 9800")]
        [InlineData(RpcWorkerConstants.PowerShellLanguageWorkerName, "-force")]
        [InlineData(RpcWorkerConstants.PythonLanguageWorkerName, "-i input")]
        [InlineData(RpcWorkerConstants.JavaLanguageWorkerName, "-D")]
        public async Task SpecializeAsync_LanguageWorkerArguments_KillsProcess(string languageWorkerName, string argument)
        {
            var testMetricsLogger = new TestMetricsLogger();
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, languageWorkerName);
            _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsExtensionVersion, "~3");
            _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteZipDeployment, "1");

            _optionsMonitor.CurrentValue.IsFileSystemReadOnly = true;

            var workerConfigs = _workerOptionsMonitor.CurrentValue.WorkerConfigs;
            workerConfigs.Add(new RpcWorkerConfig
            {
                Description = TestHelpers.GetTestWorkerDescription("powershell", ".ps1", workerIndexing: true),
                CountOptions = new WorkerProcessCountOptions()
            });
            workerConfigs.Add(new RpcWorkerConfig
            {
                Description = TestHelpers.GetTestWorkerDescription("python", ".py", workerIndexing: true),
                CountOptions = new WorkerProcessCountOptions()
            });

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{languageWorkerName}:{WorkerConstants.WorkerDescriptionArguments}"] = argument
                })
                .Build();
            _rpcWorkerChannelManager = CreateChannelManager(metrics: testMetricsLogger, config: config);

            IRpcWorkerChannel workerChannel = CreateTestChannel(languageWorkerName);

            await _rpcWorkerChannelManager.SpecializeAsync();

            // Verify logs
            var traces = _testLogger.GetLogMessages();
            Assert.True(traces.Count() == 0);

            // Verify channel
            var initializedChannel = await _rpcWorkerChannelManager.GetChannelAsync(languageWorkerName);
            Assert.Null(initializedChannel);
        }

        [Theory]
        [InlineData(RpcWorkerConstants.NodeLanguageWorkerName)]
        [InlineData(RpcWorkerConstants.PowerShellLanguageWorkerName)]
        [InlineData(RpcWorkerConstants.PythonLanguageWorkerName)]
        public async Task SpecializeAsync_Node_V2CompatibilityWithV3Extension_KillsProcess(string languageWorkerName)
        {
            var testMetricsLogger = new TestMetricsLogger();
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, languageWorkerName);
            _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsV2CompatibilityModeKey, "true");
            _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsExtensionVersion, "~3");

            _rpcWorkerChannelManager = CreateChannelManager(metrics: testMetricsLogger);

            IRpcWorkerChannel workerChannel = CreateTestChannel(languageWorkerName);

            await _rpcWorkerChannelManager.SpecializeAsync();

            // Verify logs
            var traces = _testLogger.GetLogMessages();
            Assert.True(traces.Count() == 0);

            // Verify channel
            var initializedChannel = await _rpcWorkerChannelManager.GetChannelAsync(languageWorkerName);
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
        public async Task ShutdownChannelsIfExistsAsync_StopsWorkerInvocations()
        {
            IRpcWorkerChannel javaWorkerChannel = CreateTestChannel(RpcWorkerConstants.JavaLanguageWorkerName);
            Guid invocationId = Guid.NewGuid();
            ScriptInvocationContext scriptInvocationContext = new ScriptInvocationContext()
            {
                ExecutionContext = new ExecutionContext()
                {
                    InvocationId = invocationId
                }
            };
            (javaWorkerChannel as TestRpcWorkerChannel).SendInvocationRequest(scriptInvocationContext);
            Assert.True(javaWorkerChannel.IsExecutingInvocation(invocationId.ToString()));
            Exception workerException = new Exception("Worker exception");
            // Channel is removed immediately but is not failed immediately
            await _rpcWorkerChannelManager.ShutdownChannelIfExistsAsync(RpcWorkerConstants.JavaLanguageWorkerName, javaWorkerChannel.Id, workerException);

            Assert.Null(_rpcWorkerChannelManager.GetChannels(RpcWorkerConstants.JavaLanguageWorkerName));

            var initializedChannel = await _rpcWorkerChannelManager.GetChannelAsync(RpcWorkerConstants.JavaLanguageWorkerName);
            Assert.Null(initializedChannel);
            // Execution will be terminated in the background - giving it 10 seconds
            await TestHelpers.Await(() =>
            {
                return !javaWorkerChannel.IsExecutingInvocation(invocationId.ToString());
            }, 10000);
            Assert.False(javaWorkerChannel.IsExecutingInvocation(invocationId.ToString()));
        }

        [Fact]
        public async Task InitializeLanguageWorkerChannel_ThrowsOnProcessStartup()
        {
            var rpcWorkerChannelFactory = new TestRpcWorkerChannelFactory(_eventManager, null, _scriptRootPath, throwOnProcessStartUp: true);
            var rpcWorkerChannelManager = CreateChannelManager(rpcWorkerChannelFactory);
            var rpcWorkerChannel = await rpcWorkerChannelManager.InitializeLanguageWorkerChannel(_languageWorkerOptions.WorkerConfigs, "test", _scriptRootPath);
            var ex = await Assert.ThrowsAsync<AggregateException>(async () => await rpcWorkerChannelManager.GetChannelAsync("test"));
            Assert.Contains("Process startup failed", ex.InnerException.Message);
        }

        [Fact]
        public async void WorkerWarmup_VerifyLogs()
        {
            var testMetricsLogger = new TestMetricsLogger();
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.JavaLanguageWorkerName);
            _rpcWorkerChannelManager = CreateChannelManager(metrics: testMetricsLogger);

            IRpcWorkerChannel javaWorkerChannel = CreateTestChannel(RpcWorkerConstants.JavaLanguageWorkerName);

            await _rpcWorkerChannelManager.WorkerWarmupAsync();

            // Verify logs
            var traces = _testLogger.GetLogMessages();
            var functionLoadLogs = traces.Where(m => string.Equals(m.FormattedMessage, "SendWorkerWarmupRequest called"));
            Assert.True(functionLoadLogs.Count() == 1);
        }

        private WebHostRpcWorkerChannelManager CreateChannelManager(IRpcWorkerChannelFactory channelFactory = null, IMetricsLogger metrics = null,
            IConfiguration config = null)
        {
            metrics ??= new TestMetricsLogger();
            channelFactory ??= _rpcWorkerChannelFactory;
            config ??= _emptyConfig;
            return new WebHostRpcWorkerChannelManager(_eventManager, _testEnvironment, _loggerFactory, channelFactory,
                _optionsMonitor, metrics, config, _workerProfileManager);
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
            return CreateTestChannel(language, _workerOptionsMonitor.CurrentValue.WorkerConfigs);
        }

        private IRpcWorkerChannel CreateTestChannel(string language, IList<RpcWorkerConfig> workerConfigs)
        {
            var testChannel = _rpcWorkerChannelFactory.Create(_scriptRootPath, language, null, 0, workerConfigs);
            _rpcWorkerChannelManager.AddOrUpdateWorkerChannels(language, testChannel);
            _rpcWorkerChannelManager.SetInitializedWorkerChannel(language, testChannel);
            return testChannel;
        }
    }
}