// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Abstractions;
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
            IRpcWorkerChannel testLanguageWorkerChannel = new TestRpcWorkerChannel(Guid.NewGuid().ToString(), RpcWorkerConstants.NodeLanguageWorkerName);
            _mockLanguageWorkerChannelManager.Setup(m => m.InitializeChannelAsync(It.IsAny<string>()))
                                             .Returns(Task.FromResult<IRpcWorkerChannel>(testLanguageWorkerChannel));
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
                _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.JavaLanguageWorkerName), Times.Never);
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
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName)).Returns(string.Empty);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName)).Returns("functionsPlaceholderTemplateSite");

            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _logger);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.JavaLanguageWorkerName), Times.Once);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.PythonLanguageWorkerName), Times.Never);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.NodeLanguageWorkerName), Times.Never);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.PowerShellLanguageWorkerName), Times.Never);
            Assert.Contains("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();
        }

        [Fact]
        public async Task RpcInitializationService_Initializes_RpcServerOnly_Windows_NoPlaceHolderMode()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode)).Returns("0");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName)).Returns(string.Empty);

            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _logger);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.JavaLanguageWorkerName), Times.Never);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.PythonLanguageWorkerName), Times.Never);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.NodeLanguageWorkerName), Times.Never);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.PowerShellLanguageWorkerName), Times.Never);
            Assert.Contains("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();
        }

        [Fact]
        public async Task RpcInitializationService_Initializes_RpcServerAndChannels_LinuxConsumption_PlaceHolderMode()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode)).Returns("1");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName)).Returns(string.Empty);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.ContainerName)).Returns("LinuxContainer");

            var fileSystem = new Mock<IFileSystem>();
            fileSystem.Setup(f =>
                    f.File.Exists(It.Is<string>(path => path.EndsWith(ScriptConstants.DisableContainerFileName))))
                .Returns(false);
            FileUtility.Instance = fileSystem.Object;

            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _logger);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.PythonLanguageWorkerName), Times.Once);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.NodeLanguageWorkerName), Times.Once);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.JavaLanguageWorkerName), Times.Never);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.PowerShellLanguageWorkerName), Times.Never);
            Assert.Contains("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();

            FileUtility.Instance = null;
        }

        [Fact]
        public async Task RpcInitializationService_Does_Not_Initialize_RpcServerAndChannels_LinuxConsumption_DisabledContainer()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.ContainerName)).Returns("LinuxContainer");

            var fileSystem = new Mock<IFileSystem>();
            fileSystem.Setup(f =>
                    f.File.Exists(It.Is<string>(path => path.EndsWith(ScriptConstants.DisableContainerFileName))))
                .Returns(true);
            FileUtility.Instance = fileSystem.Object;

            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _logger);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.PythonLanguageWorkerName), Times.Never);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.NodeLanguageWorkerName), Times.Never);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.JavaLanguageWorkerName), Times.Never);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.PowerShellLanguageWorkerName), Times.Never);
            Assert.DoesNotContain("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();

            FileUtility.Instance = null;
        }

        [Fact]
        public async Task RpcInitializationService_Initializes_RpcServerOnly_LinuxConsumption_NoPlaceHolderMode()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode)).Returns("0");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName)).Returns(string.Empty);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.ContainerName)).Returns("LinuxContainer");

            var fileSystem = new Mock<IFileSystem>();
            fileSystem.Setup(f =>
                    f.File.Exists(It.Is<string>(path => path.EndsWith(ScriptConstants.DisableContainerFileName))))
                .Returns(false);
            FileUtility.Instance = fileSystem.Object;

            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _logger);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.PythonLanguageWorkerName), Times.Never);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.NodeLanguageWorkerName), Times.Never);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.JavaLanguageWorkerName), Times.Never);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.PowerShellLanguageWorkerName), Times.Never);
            Assert.Contains("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();

            FileUtility.Instance = null;
        }

        [Fact]
        public async Task RpcInitializationService_Initializes_RpcServerAndChannels_LinuxAppService_PlaceHolderMode()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsLogsMountPath)).Returns(@"d:\test");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId)).Returns("1234");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode)).Returns("1");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName)).Returns(string.Empty);

            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _logger);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.PythonLanguageWorkerName), Times.Once);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.NodeLanguageWorkerName), Times.Once);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.JavaLanguageWorkerName), Times.Never);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.PowerShellLanguageWorkerName), Times.Never);
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
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName)).Returns(string.Empty);

            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _logger);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.PythonLanguageWorkerName), Times.Never);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.NodeLanguageWorkerName), Times.Never);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.JavaLanguageWorkerName), Times.Never);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.PowerShellLanguageWorkerName), Times.Never);
            Assert.Contains("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();
        }

        [Fact]
        public async Task RpcInitializationService_Initializes_RpcServerAndChannels_WebHostLevel_WorkerRuntime_Set_Node_NoPlaceHolderMode()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName)).Returns(RpcWorkerConstants.NodeLanguageWorkerName);

            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _logger);
            _rpcInitializationService.AddSupportedWebHostLevelRuntime(RpcWorkerConstants.NodeLanguageWorkerName);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.NodeLanguageWorkerName), Times.Once);
            Assert.Contains("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();
        }

        [Fact]
        public async Task RpcInitializationService_Initializes_RpcServerAndChannels_WebHostLevel_WorkerRuntime_Set_Node_PlaceHolderMode()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName)).Returns(RpcWorkerConstants.NodeLanguageWorkerName);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode)).Returns("1");

            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _logger);
            _rpcInitializationService.AddSupportedWebHostLevelRuntime(RpcWorkerConstants.NodeLanguageWorkerName);

            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.NodeLanguageWorkerName), Times.Once);
            Assert.Contains("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();
        }

        [Fact]
        public async Task RpcInitializationService_Initializes_RpcServerOnly_WebHostLevel_WorkerRuntime_Set_RuntimeNotSupported()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName)).Returns(RpcWorkerConstants.NodeLanguageWorkerName);

            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _logger);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.NodeLanguageWorkerName), Times.Never);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.JavaLanguageWorkerName), Times.Never);
            Assert.Contains("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();
        }

        [Fact]
        public async Task RpcInitializationService_Initializes_RpcServerOnly_WebHostLevel_WorkerRuntime_NotSet_NoPlaceHolderMode()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();

            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _logger);
            _rpcInitializationService.AddSupportedWebHostLevelRuntime(RpcWorkerConstants.NodeLanguageWorkerName);

            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.JavaLanguageWorkerName), Times.Never);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.NodeLanguageWorkerName), Times.Never);
            Assert.Contains("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();
        }

        [Fact]
        public async Task RpcInitializationService_Initializes_RpcServerOnly_WorkerRuntime_Set_Python_NoPlaceHolderMode()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName)).Returns(RpcWorkerConstants.PythonLanguageWorkerName);

            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _logger);

            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(RpcWorkerConstants.PythonLanguageWorkerName), Times.Never);
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

        [Theory]
        [InlineData("1", "functionsPlaceholderTemplateSite", "1234", true)]
        [InlineData("0", "functionsPlaceholderTemplateSite", "1234", false)]
        [InlineData("1", "functionsPlaceholderTemplateSitejava", "1234", false)]
        [InlineData("1", "functionsPlaceholderTemplateSite", "", true)]
        public void ShouldStartStandbyPlaceholderChannels_Returns_ExpectedValue(string placeholderMode, string siteName, string siteInstanaceId, bool expectedResult)
        {
            Mock<IRpcServer> testRpcServer = new Mock<IRpcServer>();
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode)).Returns(placeholderMode);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName)).Returns(siteName);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId)).Returns(siteInstanaceId);
            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer.Object, _mockLanguageWorkerChannelManager.Object, _logger);
            Assert.Equal(expectedResult, _rpcInitializationService.ShouldStartStandbyPlaceholderChannels());
        }

        [Theory]
        [InlineData("1", "node", "1234", true)]
        [InlineData("0", "node", "1234", false)]
        [InlineData("1", "node", "", true)]
        [InlineData("1", "Node", "", true)]
        [InlineData("1", "powershell", "1234", true)]
        [InlineData("0", "powershell", "1234", false)]
        [InlineData("1", "powershell", "", true)]
        [InlineData("1", "Powershell", "", true)]
        [InlineData("1", "java", "1234", true)]
        [InlineData("0", "java", "1234", false)]
        [InlineData("0", "JAVA", "1234", false)]
        [InlineData("1", "java", "", true)]
        [InlineData("1", "", "1234", false)]
        [InlineData("1", "dotnet", "1234", false)]
        [InlineData("1", "python", "1234", false)]
        public void ShouldStartAsPlaceholderPool_Returns_ExpectedValue(string placeholderMode, string workerRuntime, string siteInstanaceId, bool expectedResult)
        {
            Mock<IRpcServer> testRpcServer = new Mock<IRpcServer>();
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode)).Returns(placeholderMode);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName)).Returns(workerRuntime);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId)).Returns(siteInstanaceId);
            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer.Object, _mockLanguageWorkerChannelManager.Object, _logger);
            Assert.Equal(expectedResult, _rpcInitializationService.ShouldStartAsPlaceholderPool());
        }
    }
}