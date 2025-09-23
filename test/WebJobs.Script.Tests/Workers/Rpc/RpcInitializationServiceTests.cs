// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class RpcInitializationServiceTests
    {
        private IOptionsMonitor<ScriptApplicationHostOptions> _optionsMonitor;
        private IOptionsMonitor<LanguageWorkerOptions> _workerOptionsMonitor;
        private RpcInitializationService _rpcInitializationService;
        private Mock<IWebHostRpcWorkerChannelManager> _mockLanguageWorkerChannelManager;
        private LoggerFactory _loggerFactory;
        private ILogger<RpcInitializationService> _logger;

        public RpcInitializationServiceTests()
        {
            _mockLanguageWorkerChannelManager = new Mock<IWebHostRpcWorkerChannelManager>();
            _loggerFactory = new LoggerFactory();
            _logger = _loggerFactory.CreateLogger<RpcInitializationService>();

            var applicationHostOptions = new ScriptApplicationHostOptions
            {
                IsSelfHost = true,
                ScriptPath = Path.GetTempPath()
            };
            _optionsMonitor = TestHelpers.CreateOptionsMonitor(applicationHostOptions);
            _workerOptionsMonitor = TestHelpers.CreateOptionsMonitor(TestHelpers.GetTestLanguageWorkerOptions());

            IRpcWorkerChannel testLanguageWorkerChannel = new TestRpcWorkerChannel(Guid.NewGuid().ToString(), RpcWorkerConstants.NodeLanguageWorkerName);
            _mockLanguageWorkerChannelManager.Setup(m => m.InitializeChannelAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), It.IsAny<string>()))
                                             .Returns(Task.FromResult<IRpcWorkerChannel>(testLanguageWorkerChannel));
        }

        [Fact]
        public async Task RpcInitializationService_AppOffline()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();
            var mockWorkerRuntimeResolver = new Mock<IWorkerRuntimeResolver>(MockBehavior.Strict);
            mockWorkerRuntimeResolver.Setup(p => p.GetWorkerRuntime(It.IsAny<string>())).Returns(RpcWorkerConstants.JavaLanguageWorkerName);
            string offlineFilePath = null;
            try
            {
                offlineFilePath = TestHelpers.CreateOfflineFile();
                _rpcInitializationService = new RpcInitializationService(
                    _optionsMonitor,
                    mockEnvironment.Object,
                    testRpcServer,
                    _mockLanguageWorkerChannelManager.Object,
                    _logger,
                    _workerOptionsMonitor,
                    mockWorkerRuntimeResolver.Object);
                await _rpcInitializationService.StartAsync(CancellationToken.None);
                _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), RpcWorkerConstants.JavaLanguageWorkerName), Times.Never);
                Assert.DoesNotContain("testserver", testRpcServer.Uri.ToString());
                await testRpcServer.ShutdownAsync();
            }
            finally
            {
                TestHelpers.DeleteTestFile(offlineFilePath);
            }
        }

        [Fact]
        public async Task RpcInitializationService_Initializes_RpcServerAndChannels_WorkerRuntime_Set_Node_NoPlaceHolderMode()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime)).Returns(RpcWorkerConstants.NodeLanguageWorkerName);
            var mockWorkerRuntimeResolver = new Mock<IWorkerRuntimeResolver>(MockBehavior.Strict);
            mockWorkerRuntimeResolver.Setup(p => p.GetWorkerRuntime(It.IsAny<string>())).Returns(RpcWorkerConstants.NodeLanguageWorkerName);

            _rpcInitializationService = new RpcInitializationService(
                _optionsMonitor,
                mockEnvironment.Object,
                testRpcServer,
                _mockLanguageWorkerChannelManager.Object,
                _logger,
                _workerOptionsMonitor,
                mockWorkerRuntimeResolver.Object);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), RpcWorkerConstants.NodeLanguageWorkerName), Times.Never);
            Assert.Contains("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();
        }

        [Fact]
        public async Task RpcInitializationService_Initializes_RpcServerAndChannels_WebHostLevel_WorkerRuntime_Set_Node_PlaceHolderMode()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime)).Returns(RpcWorkerConstants.NodeLanguageWorkerName);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode)).Returns("1");

            var mockWorkerRuntimeResolver = new Mock<IWorkerRuntimeResolver>(MockBehavior.Strict);
            mockWorkerRuntimeResolver.Setup(p => p.GetWorkerRuntime(It.IsAny<string>())).Returns(RpcWorkerConstants.NodeLanguageWorkerName);

            _rpcInitializationService = new RpcInitializationService(
                _optionsMonitor,
                mockEnvironment.Object,
                testRpcServer,
                _mockLanguageWorkerChannelManager.Object,
                _logger,
                _workerOptionsMonitor,
                mockWorkerRuntimeResolver.Object);

            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), RpcWorkerConstants.NodeLanguageWorkerName), Times.Once);
            Assert.Contains("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();
        }

        [Theory]
        [InlineData("")]
        [InlineData("node")]
        public async Task RpcInitializationService_Initializes_RpcServer_RpcChannels_PlaceholderMode(string workerRuntime)
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode)).Returns("1");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime)).Returns(workerRuntime);
            if (string.IsNullOrEmpty(workerRuntime))
            {
                mockEnvironment.Setup(p => p.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerPlaceholderModeListSettingName)).Returns($"{RpcWorkerConstants.PythonLanguageWorkerName};{RpcWorkerConstants.JavaLanguageWorkerName};{RpcWorkerConstants.NodeLanguageWorkerName}");
            }
            else
            {
                mockEnvironment.Setup(p => p.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerPlaceholderModeListSettingName)).Returns($"{RpcWorkerConstants.PythonLanguageWorkerName};{RpcWorkerConstants.JavaLanguageWorkerName};");
            }

            var mockWorkerRuntimeResolver = new Mock<IWorkerRuntimeResolver>(MockBehavior.Strict);
            mockWorkerRuntimeResolver.Setup(p => p.GetWorkerRuntime(It.IsAny<string>())).Returns(RpcWorkerConstants.NodeLanguageWorkerName);

            _rpcInitializationService = new RpcInitializationService(
                _optionsMonitor,
                mockEnvironment.Object,
                testRpcServer,
                _mockLanguageWorkerChannelManager.Object,
                _logger,
                _workerOptionsMonitor,
                mockWorkerRuntimeResolver.Object);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), RpcWorkerConstants.NodeLanguageWorkerName), Times.Once);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), RpcWorkerConstants.JavaLanguageWorkerName), Times.Once);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), RpcWorkerConstants.PythonLanguageWorkerName), Times.Once);
            Assert.Contains("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();
        }

        [Fact]
        public async Task RpcInitializationService_Initializes_RpcServer_DoesNot_Initialize_RpcChannels_NoPlaceholderMode()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime)).Returns(RpcWorkerConstants.NodeLanguageWorkerName);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerPlaceholderModeListSettingName)).Returns($"{RpcWorkerConstants.PythonLanguageWorkerName};{RpcWorkerConstants.JavaLanguageWorkerName};");

            var mockWorkerRuntimeResolver = new Mock<IWorkerRuntimeResolver>(MockBehavior.Strict);
            mockWorkerRuntimeResolver.Setup(p => p.GetWorkerRuntime(It.IsAny<string>())).Returns(RpcWorkerConstants.NodeLanguageWorkerName);

            _rpcInitializationService = new RpcInitializationService(
                _optionsMonitor,
                mockEnvironment.Object,
                testRpcServer,
                _mockLanguageWorkerChannelManager.Object,
                _logger,
                _workerOptionsMonitor,
                mockWorkerRuntimeResolver.Object);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), RpcWorkerConstants.NodeLanguageWorkerName), Times.Never);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), RpcWorkerConstants.JavaLanguageWorkerName), Times.Never);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), RpcWorkerConstants.PythonLanguageWorkerName), Times.Never);
            Assert.Contains("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();
        }

        [Fact]
        public async Task RpcInitializationService_Stops_DoesNotStopRpcServer()
        {
            var testRpcServer = new Mock<IRpcServer>();
            var mockWorkerRuntimeResolver = new Mock<IWorkerRuntimeResolver>(MockBehavior.Strict);
            mockWorkerRuntimeResolver.Setup(p => p.GetWorkerRuntime(It.IsAny<string>())).Returns(RpcWorkerConstants.NodeLanguageWorkerName);

            _rpcInitializationService = new RpcInitializationService(
                _optionsMonitor,
                new Mock<IEnvironment>().Object,
                testRpcServer.Object,
                _mockLanguageWorkerChannelManager.Object,
                _logger,
                _workerOptionsMonitor,
                mockWorkerRuntimeResolver.Object);
            await _rpcInitializationService.StopAsync(CancellationToken.None);
            testRpcServer.Verify(a => a.KillAsync(), Times.Never);
            testRpcServer.Verify(a => a.ShutdownAsync(), Times.Never);
        }

        [Fact]
        public async Task RpcInitializationService_TriggerShutdown()
        {
            var mockWorkerRuntimeResolver = new Mock<IWorkerRuntimeResolver>();
            mockWorkerRuntimeResolver.Setup(p => p.GetWorkerRuntime(It.IsAny<string>())).Returns(RpcWorkerConstants.NodeLanguageWorkerName);

            Mock<IRpcServer> testRpcServer = new Mock<IRpcServer>();
            _rpcInitializationService = new RpcInitializationService(
                _optionsMonitor,
                new Mock<IEnvironment>().Object,
                testRpcServer.Object,
                _mockLanguageWorkerChannelManager.Object,
                _logger,
                _workerOptionsMonitor,
                mockWorkerRuntimeResolver.Object);
            await _rpcInitializationService.OuterStopAsync(CancellationToken.None);
            testRpcServer.Verify(a => a.ShutdownAsync(), Times.Once);
            testRpcServer.Verify(a => a.KillAsync(), Times.Never);
        }

        [Fact]
        public async Task RpcInitializationService_TriggerShutdown_KillGetsCalledWhenShutdownTimesout()
        {
            var mockWorkerRuntimeResolver = new Mock<IWorkerRuntimeResolver>();
            mockWorkerRuntimeResolver.Setup(p => p.GetWorkerRuntime(It.IsAny<string>())).Returns(RpcWorkerConstants.NodeLanguageWorkerName);

            Mock<IRpcServer> testRpcServer = new Mock<IRpcServer>();
            testRpcServer.Setup(a => a.ShutdownAsync()).Returns(Task.Delay(6000));
            _rpcInitializationService = new RpcInitializationService(
                _optionsMonitor,
                new Mock<IEnvironment>().Object,
                testRpcServer.Object,
                _mockLanguageWorkerChannelManager.Object,
                _logger,
                _workerOptionsMonitor,
                mockWorkerRuntimeResolver.Object);
            await _rpcInitializationService.OuterStopAsync(CancellationToken.None);
            testRpcServer.Verify(a => a.ShutdownAsync(), Times.Once);
            testRpcServer.Verify(a => a.KillAsync(), Times.Once);
        }

        [Fact]
        public async Task RpcInitializationService_TriggerShutdown_KillGetsCalledWhenShutdownThrowsException()
        {
            var mockWorkerRuntimeResolver = new Mock<IWorkerRuntimeResolver>();
            mockWorkerRuntimeResolver.Setup(p => p.GetWorkerRuntime(It.IsAny<string>())).Returns(RpcWorkerConstants.NodeLanguageWorkerName);
            Mock<IRpcServer> testRpcServer = new Mock<IRpcServer>();
            testRpcServer.Setup(a => a.ShutdownAsync()).ThrowsAsync(new Exception("Random Exception"));
            _rpcInitializationService = new RpcInitializationService(
                _optionsMonitor,
                new Mock<IEnvironment>().Object,
                testRpcServer.Object,
                _mockLanguageWorkerChannelManager.Object,
                _logger,
                _workerOptionsMonitor,
                mockWorkerRuntimeResolver.Object);
            await _rpcInitializationService.OuterStopAsync(CancellationToken.None);
            testRpcServer.Verify(a => a.ShutdownAsync(), Times.Once);
            testRpcServer.Verify(a => a.KillAsync(), Times.Once);
        }
    }
}