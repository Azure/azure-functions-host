// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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
        private RpcInitializationService _rpcInitializationService;
        private IOptionsMonitor<ScriptApplicationHostOptions> _optionsMonitor;
        private Mock<ILanguageWorkerChannelManager> _mockLanguageWorkerChannelManager;
        private LoggerFactory _loggerFactory;

        public RpcInitializationServiceTests()
        {
            _mockLanguageWorkerChannelManager = new Mock<ILanguageWorkerChannelManager>();
            _loggerFactory = new LoggerFactory();

            var applicationHostOptions = new ScriptApplicationHostOptions
            {
                IsSelfHost = true,
                ScriptPath = Path.GetTempPath()
            };
            _optionsMonitor = TestHelpers.CreateOptionsMonitor(applicationHostOptions);
            _mockLanguageWorkerChannelManager.Setup(m => m.InitializeChannelAsync(It.IsAny<string>()))
                                             .Returns(Task.CompletedTask);
        }

        [Fact]
        public async Task RpcInitializationService_Initializes_RpcServerAndChannels_PlaceHolderMode()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode)).Returns("1");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName)).Returns(string.Empty);

            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _loggerFactory);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.JavaLanguageWorkerName), Times.Once);
            Assert.Contains("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();
        }

        [Fact]
        public async Task RpcInitializationService_LinuxConsumption_Initializes_RpcServer()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.ContainerName)).Returns("testContainer");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName)).Returns("java");

            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _loggerFactory);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.JavaLanguageWorkerName), Times.Never);
            Assert.Contains("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();
        }

        [Fact]
        public async Task RpcInitializationService_LinuxAppService_Initializes_RpcServer()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsLogsMountPath)).Returns(@"d:\test");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId)).Returns("1234");

            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _loggerFactory);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.JavaLanguageWorkerName), Times.Never);
            Assert.Contains("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();
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
                _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _loggerFactory);
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
        public async Task RpcInitializationService_Initializes_WorkerRuntime_Set()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName)).Returns(LanguageWorkerConstants.NodeLanguageWorkerName);

            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _loggerFactory);
            _rpcInitializationService.AddSupportedRuntime(LanguageWorkerConstants.NodeLanguageWorkerName);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.NodeLanguageWorkerName), Times.Once);
            Assert.Contains("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();
        }

        [Fact]
        public async Task RpcInitializationService_Initializes_WorkerRuntime_Set_RuntimeNotSupported()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName)).Returns(LanguageWorkerConstants.NodeLanguageWorkerName);

            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _loggerFactory);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.NodeLanguageWorkerName), Times.Never);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.JavaLanguageWorkerName), Times.Never);
            Assert.Contains("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();
        }

        [Fact]
        public async Task RpcInitializationService_Initializes_WorkerRuntime_NotSet_NoPlaceholder()
        {
            IRpcServer testRpcServer = new TestRpcServer();
            var mockEnvironment = new Mock<IEnvironment>();

            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, mockEnvironment.Object, testRpcServer, _mockLanguageWorkerChannelManager.Object, _loggerFactory);
            _rpcInitializationService.AddSupportedRuntime(LanguageWorkerConstants.NodeLanguageWorkerName);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.JavaLanguageWorkerName), Times.Never);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.NodeLanguageWorkerName), Times.Never);
            Assert.Contains("testserver", testRpcServer.Uri.ToString());
            await testRpcServer.ShutdownAsync();
        }
    }
}