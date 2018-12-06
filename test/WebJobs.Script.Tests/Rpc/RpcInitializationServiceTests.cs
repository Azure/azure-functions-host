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
        private IRpcServer _testRpcServer;
        private LoggerFactory _loggerFactory;
        private string _rootPath;

        public RpcInitializationServiceTests()
        {
            _rootPath = Path.GetTempPath();
            _mockLanguageWorkerChannelManager = new Mock<ILanguageWorkerChannelManager>();
            _loggerFactory = new LoggerFactory();
            _testRpcServer = new TestRpcServer();
            var applicationHostOptions = new ScriptApplicationHostOptions
            {
                IsSelfHost = true,
                ScriptPath = _rootPath
            };
            _optionsMonitor = TestHelpers.CreateOptionsMonitor(applicationHostOptions);
            _mockLanguageWorkerChannelManager.Setup(m => m.InitializeChannelAsync(It.IsAny<string>()))
                                             .Returns(Task.CompletedTask);
        }

        [Fact]
        public async Task RpcInitializationService_Initializes_RpcServerAndChannels_PlaceHolderMode()
        {
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, testEnvironment, _testRpcServer, _mockLanguageWorkerChannelManager.Object, _loggerFactory);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.JavaLanguageWorkerName), Times.Once);
            Assert.Contains("testserver", _testRpcServer.Uri.ToString());
        }

        [Fact]
        public async Task RpcInitializationService_LinuxConsumption_Initializes_RpcServer()
        {
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, "testContainer");
            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, testEnvironment, _testRpcServer, _mockLanguageWorkerChannelManager.Object, _loggerFactory);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.JavaLanguageWorkerName), Times.Never);
            Assert.Contains("testserver", _testRpcServer.Uri.ToString());
        }

        [Fact]
        public async Task RpcInitializationService_LinuxAppService_Initializes_RpcServer()
        {
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsLogsMountPath, "d:\\test\\mount");
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId, "1234");
            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, testEnvironment, _testRpcServer, _mockLanguageWorkerChannelManager.Object, _loggerFactory);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.JavaLanguageWorkerName), Times.Never);
            Assert.Contains("testserver", _testRpcServer.Uri.ToString());
        }

        [Fact]
        public async Task RpcInitializationService_AppOffline()
        {
            var testEnvironment = new TestEnvironment();
            var offlineFilePath = TestHelpers.CreateOfflineFile(_rootPath);
            try
            {
                _rpcInitializationService = new RpcInitializationService(_optionsMonitor, testEnvironment, _testRpcServer, _mockLanguageWorkerChannelManager.Object, _loggerFactory);
                await _rpcInitializationService.StartAsync(CancellationToken.None);
                _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.JavaLanguageWorkerName), Times.Never);
                Assert.DoesNotContain("testserver", _testRpcServer.Uri.ToString());
            }
            finally
            {
                DeleteTestFile(offlineFilePath);
            }
        }

        [Fact]
        public async Task RpcInitializationService_Initializes_WorkerRuntime_Set()
        {
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.NodeLanguageWorkerName);
            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, testEnvironment, _testRpcServer, _mockLanguageWorkerChannelManager.Object, _loggerFactory);
            _rpcInitializationService.AddSupportedRuntime(LanguageWorkerConstants.NodeLanguageWorkerName);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.NodeLanguageWorkerName), Times.Once);
            Assert.Contains("testserver", _testRpcServer.Uri.ToString());
        }

        [Fact]
        public async Task RpcInitializationService_Initializes_WorkerRuntime_Set_RuntimeNotSupported()
        {
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.NodeLanguageWorkerName);
            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, testEnvironment, _testRpcServer, _mockLanguageWorkerChannelManager.Object, _loggerFactory);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.NodeLanguageWorkerName), Times.Never);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.JavaLanguageWorkerName), Times.Never);
            Assert.Contains("testserver", _testRpcServer.Uri.ToString());
        }

        [Fact]
        public async Task RpcInitializationService_Initializes_WorkerRuntime_NotSet_NoPlaceholder()
        {
            var testEnvironment = new TestEnvironment();
            _rpcInitializationService = new RpcInitializationService(_optionsMonitor, testEnvironment, _testRpcServer, _mockLanguageWorkerChannelManager.Object, _loggerFactory);
            _rpcInitializationService.AddSupportedRuntime(LanguageWorkerConstants.NodeLanguageWorkerName);
            await _rpcInitializationService.StartAsync(CancellationToken.None);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.JavaLanguageWorkerName), Times.Never);
            _mockLanguageWorkerChannelManager.Verify(m => m.InitializeChannelAsync(LanguageWorkerConstants.NodeLanguageWorkerName), Times.Never);
            Assert.Contains("testserver", _testRpcServer.Uri.ToString());
        }

        private static void DeleteTestFile(string testFile)
        {
            if (File.Exists(testFile))
            {
                try
                {
                    File.Delete(testFile);
                }
                catch
                {
                    // best effort cleanup
                }
            }
        }
    }
}
