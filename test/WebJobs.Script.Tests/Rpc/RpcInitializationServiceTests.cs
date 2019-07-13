// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Rpc
{
    public class RpcInitializationServiceTests
    {
        private IOptionsMonitor<ScriptApplicationHostOptions> _optionsMonitor;
        private RpcInitializationService _rpcInitializationService;
        private Mock<IWebHostLanguageWorkerChannelManager> _mockLanguageWorkerChannelManager;
        private LoggerFactory _loggerFactory;
        private ILogger<RpcInitializationService> _logger;

        public RpcInitializationServiceTests()
        {
            _mockLanguageWorkerChannelManager = new Mock<IWebHostLanguageWorkerChannelManager>();
            _loggerFactory = new LoggerFactory();
            _logger = _loggerFactory.CreateLogger<RpcInitializationService>();

            var applicationHostOptions = new ScriptApplicationHostOptions
            {
                IsSelfHost = true,
                ScriptPath = Path.GetTempPath()
            };
            _optionsMonitor = TestHelpers.CreateOptionsMonitor(applicationHostOptions);
            ILanguageWorkerChannel testLanguageWorkerChannel = new TestLanguageWorkerChannel(Guid.NewGuid().ToString(), LanguageWorkerConstants.NodeLanguageWorkerName);
            _mockLanguageWorkerChannelManager.Setup(m => m.InitializeChannelAsync(It.IsAny<string>()))
                                             .Returns(Task.FromResult<ILanguageWorkerChannel>(testLanguageWorkerChannel));
        }

        [Fact]
        public async Task RpcInitializationService_AppOffline()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();
            string offlineFilePath = null;
            try
            {
                offlineFilePath = TestHelpers.CreateOfflineFile();
                _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _logger);
                await _rpcInitializationService.StartAsync(CancellationToken.None);
                _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.JavaLanguageWorkerName), Times.Never);
                Assert.DoesNotContain("testserver", testRpcServer.Uri.ToString());
                await testRpcServer.ShutdownAsync();
            }
            finally
            {
                TestHelpers.DeleteTestFile(offlineFilePath);
            }
        }

        [Fact]
        public async Task RpcInitializationService_Initializes_RpcServerAndChannels_Windows_PlaceHolderMode()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode)).Returns("1");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName)).Returns(string.Empty);

            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _logger);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.JavaLanguageWorkerName), Times.Once);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.PythonLanguageWorkerName), Times.Never);
            Assert.Contains("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();
        }

        [Fact]
        public async Task RpcInitializationService_Initializes_RpcServerOnly_Windows_NoPlaceHolderMode()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode)).Returns("0");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName)).Returns(string.Empty);

            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _logger);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.JavaLanguageWorkerName), Times.Never);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.PythonLanguageWorkerName), Times.Never);
            Assert.Contains("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();
        }

        [Fact]
        public async Task RpcInitializationService_Initializes_RpcServerAndChannels_LinuxConsumption_PlaceHolderMode()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode)).Returns("1");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName)).Returns(string.Empty);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.ContainerName)).Returns("LinuxContainer");

            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _logger);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.PythonLanguageWorkerName), Times.Once);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.JavaLanguageWorkerName), Times.Never);
            Assert.Contains("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();
        }

        [Fact]
        public async Task RpcInitializationService_Initializes_RpcServerOnly_LinuxConsumption_NoPlaceHolderMode()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode)).Returns("0");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName)).Returns(string.Empty);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.ContainerName)).Returns("LinuxContainer");

            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _logger);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.PythonLanguageWorkerName), Times.Never);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.JavaLanguageWorkerName), Times.Never);
            Assert.Contains("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();
        }

        [Fact]
        public async Task RpcInitializationService_Initializes_RpcServerAndChannels_LinuxAppService_PlaceHolderMode()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsLogsMountPath)).Returns(@"d:\test");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId)).Returns("1234");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode)).Returns("1");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName)).Returns(string.Empty);

            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _logger);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.PythonLanguageWorkerName), Times.Once);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.JavaLanguageWorkerName), Times.Never);
            Assert.Contains("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();
        }

        [Fact]
        public async Task RpcInitializationService_Initializes_RpcServerOnly_LinuxAppService_NoPlaceHolderMode()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsLogsMountPath)).Returns(@"d:\test");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId)).Returns("1234");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode)).Returns("0");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName)).Returns(string.Empty);

            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _logger);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.PythonLanguageWorkerName), Times.Never);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.JavaLanguageWorkerName), Times.Never);
            Assert.Contains("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();
        }

        [Fact]
        public async Task RpcInitializationService_Initializes_RpcServerAndChannels_WebHostLevel_WorkerRuntime_Set_Node_NoPlaceHolderMode()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName)).Returns(LanguageWorkerConstants.NodeLanguageWorkerName);

            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _logger);
            _rpcInitializationService.AddSupportedWebHostLevelRuntime(LanguageWorkerConstants.NodeLanguageWorkerName);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.NodeLanguageWorkerName), Times.Once);
            Assert.Contains("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();
        }

        [Fact]
        public async Task RpcInitializationService_Initializes_RpcServerAndChannels_WebHostLevel_WorkerRuntime_Set_Node_PlaceHolderMode()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName)).Returns(LanguageWorkerConstants.NodeLanguageWorkerName);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode)).Returns("1");

            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _logger);
            _rpcInitializationService.AddSupportedWebHostLevelRuntime(LanguageWorkerConstants.NodeLanguageWorkerName);

            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.NodeLanguageWorkerName), Times.Once);
            Assert.Contains("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();
        }

        [Fact]
        public async Task RpcInitializationService_Initializes_RpcServerOnly_WebHostLevel_WorkerRuntime_Set_RuntimeNotSupported()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName)).Returns(LanguageWorkerConstants.NodeLanguageWorkerName);

            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _logger);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.NodeLanguageWorkerName), Times.Never);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.JavaLanguageWorkerName), Times.Never);
            Assert.Contains("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();
        }

        [Fact]
        public async Task RpcInitializationService_Initializes_RpcServerOnly_WebHostLevel_WorkerRuntime_NotSet_NoPlaceHolderMode()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();

            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _logger);
            _rpcInitializationService.AddSupportedWebHostLevelRuntime(LanguageWorkerConstants.NodeLanguageWorkerName);

            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.JavaLanguageWorkerName), Times.Never);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.NodeLanguageWorkerName), Times.Never);
            Assert.Contains("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();
        }

        [Fact]
        public async Task RpcInitializationService_Initializes_RpcServerOnly_WorkerRuntime_Set_Python_NoPlaceHolderMode()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName)).Returns(LanguageWorkerConstants.PythonLanguageWorkerName);

            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _logger);

            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.PythonLanguageWorkerName), Times.Never);
            Assert.Contains("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();
        }

        [Fact]
        public async Task RpcInitializationService_Stops_DoesNotStopRpcServer()
        {
            var testRpcServer = new Mock<IRpcServer>();
            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, new Mock<IEnvironment>().Object, testRpcServer.Object, _mockLanguageWorkerChannelManager.Object, _logger);
            await _rpcInitializationService.StopAsync(CancellationToken.None);
            testRpcServer.Verify(a => a.KillAsync(), Times.Never);
            testRpcServer.Verify(a => a.ShutdownAsync(), Times.Never);
        }

        [Fact]
        public async Task RpcInitializationService_TriggerShutdown()
        {
            Mock<IRpcServer> testRpcServer = new Mock<IRpcServer>();
            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, new Mock<IEnvironment>().Object, testRpcServer.Object, _mockLanguageWorkerChannelManager.Object, _logger);
            await _rpcInitializationService.OuterStopAsync(CancellationToken.None);
            testRpcServer.Verify(a => a.ShutdownAsync(), Times.Once);
            testRpcServer.Verify(a => a.KillAsync(), Times.Never);
        }

        [Fact]
        public async Task RpcInitializationService_TriggerShutdown_KillGetsCalledWhenShutdownTimesout()
        {
            Mock<IRpcServer> testRpcServer = new Mock<IRpcServer>();
            testRpcServer.Setup(a => a.ShutdownAsync()).Returns(Task.Delay(6000));
            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, new Mock<IEnvironment>().Object, testRpcServer.Object, _mockLanguageWorkerChannelManager.Object, _logger);
            await _rpcInitializationService.OuterStopAsync(CancellationToken.None);
            testRpcServer.Verify(a => a.ShutdownAsync(), Times.Once);
            testRpcServer.Verify(a => a.KillAsync(), Times.Once);
        }

        [Fact]
        public async Task RpcInitializationService_TriggerShutdown_KillGetsCalledWhenShutdownThrowsException()
        {
            Mock<IRpcServer> testRpcServer = new Mock<IRpcServer>();
            testRpcServer.Setup(a => a.ShutdownAsync()).ThrowsAsync(new Exception("Random Exception"));
            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, new Mock<IEnvironment>().Object, testRpcServer.Object, _mockLanguageWorkerChannelManager.Object, _logger);
            await _rpcInitializationService.OuterStopAsync(CancellationToken.None);
            testRpcServer.Verify(a => a.ShutdownAsync(), Times.Once);
            testRpcServer.Verify(a => a.KillAsync(), Times.Once);
        }
    }
}