// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Middleware
{
    public class HostWarmupMiddlewareTests
    {
        private readonly IScriptEventManager _eventManager;
        private readonly IEnvironment _testEnvironment;
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

        private WebHostRpcWorkerChannelManager _rpcWorkerChannelManager;
        private IWorkerProfileManager _workerProfileManager;

        private string _scriptRootPath = @"c:\testing\FUNCTIONS-TEST";
        private IDictionary<string, string> _capabilities = new Dictionary<string, string>()
            {
                { "StandbyModeEnabled", "true" }
            };

        private IOptions<FunctionsHostingConfigOptions> _functionsHostingConfigOptions;

        public HostWarmupMiddlewareTests()
        {
            _functionsHostingConfigOptions = Options.Create(new FunctionsHostingConfigOptions());
            _eventManager = new ScriptEventManager();
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
            _rpcWorkerChannelManager = new WebHostRpcWorkerChannelManager(_eventManager, _testEnvironment, _loggerFactory, _rpcWorkerChannelFactory, _optionsMonitor, new TestMetricsLogger(), _emptyConfig, _workerProfileManager);
        }

        [Fact]
        public void IsWarmUpRequest_ReturnsExpectedValue()
        {
            var environment = new TestEnvironment();
            var hostEnvironment = new ScriptWebHostEnvironment(environment);
            var request = HttpTestHelpers.CreateHttpRequest("POST", "http://azure.com/api/warmup");

            Assert.False(HostWarmupMiddleware.IsWarmUpRequest(request, hostEnvironment.InStandbyMode, environment));

            // Reset environment
            environment.Clear();
            hostEnvironment = new ScriptWebHostEnvironment(environment);

            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            Assert.False(HostWarmupMiddleware.IsWarmUpRequest(request, hostEnvironment.InStandbyMode, environment));

            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId, "12345");
            Assert.True(HostWarmupMiddleware.IsWarmUpRequest(request, hostEnvironment.InStandbyMode, environment));

            request = HttpTestHelpers.CreateHttpRequest("POST", "http://azure.com/api/csharphttpwarmup");
            Assert.True(HostWarmupMiddleware.IsWarmUpRequest(request, hostEnvironment.InStandbyMode, environment));

            request = HttpTestHelpers.CreateHttpRequest("POST", "http://azure.com/api/warmup");
            request.Headers.Add(ScriptConstants.AntaresLogIdHeaderName, "xyz123");
            Assert.False(HostWarmupMiddleware.IsWarmUpRequest(request, hostEnvironment.InStandbyMode, environment));

            request = HttpTestHelpers.CreateHttpRequest("POST", "http://azure.com/api/foo");
            Assert.False(HostWarmupMiddleware.IsWarmUpRequest(request, hostEnvironment.InStandbyMode, environment));

            // Reset environment
            environment.Clear();
            hostEnvironment = new ScriptWebHostEnvironment(environment);

            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            Assert.False(HostWarmupMiddleware.IsWarmUpRequest(request, hostEnvironment.InStandbyMode, environment));

            request = HttpTestHelpers.CreateHttpRequest("POST", "http://azure.com/api/warmup");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, "TestContainer");
            Assert.True(environment.IsAnyLinuxConsumption());
            Assert.True(environment.IsLinuxConsumptionOnAtlas());
            Assert.False(environment.IsFlexConsumptionSku());
            Assert.True(HostWarmupMiddleware.IsWarmUpRequest(request, hostEnvironment.InStandbyMode, environment));

            // Reset environment
            environment.Clear();
            hostEnvironment = new ScriptWebHostEnvironment(environment);

            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            Assert.False(HostWarmupMiddleware.IsWarmUpRequest(request, hostEnvironment.InStandbyMode, environment));

            request = HttpTestHelpers.CreateHttpRequest("POST", "http://azure.com/api/warmup");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, "TestContainer");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.LegionServiceHost, "1");
            Assert.True(environment.IsAnyLinuxConsumption());
            Assert.False(environment.IsLinuxConsumptionOnAtlas());
            Assert.True(environment.IsFlexConsumptionSku());
            Assert.True(HostWarmupMiddleware.IsWarmUpRequest(request, hostEnvironment.InStandbyMode, environment));
        }

        [Fact]
        public void ReadRuntimeAssemblyFiles_VerifyLogs()
        {
            var environment = new TestEnvironment();
            var hostEnvironment = new ScriptWebHostEnvironment(environment);
            var testLoggerFactory = new LoggerFactory();
            TestLoggerProvider testLoggerProvider = new TestLoggerProvider();
            testLoggerFactory.AddProvider(testLoggerProvider);
            ILogger<HostWarmupMiddleware> testLogger = testLoggerFactory.CreateLogger<HostWarmupMiddleware>();

            HostWarmupMiddleware hostWarmupMiddleware = new HostWarmupMiddleware(null, new Mock<IScriptWebHostEnvironment>().Object, environment, new Mock<IScriptHostManager>().Object, testLogger, _rpcWorkerChannelManager, _functionsHostingConfigOptions);

            hostWarmupMiddleware.ReadRuntimeAssemblyFiles();
            // Assert
            var traces = testLoggerProvider.GetAllLogMessages();
            Assert.True(traces.Any(m => m.FormattedMessage.Contains("Number of files read:")));
        }
    }
}
