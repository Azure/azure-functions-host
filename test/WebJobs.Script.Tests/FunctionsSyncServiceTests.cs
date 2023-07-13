// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionsSyncServiceTests
    {
        private readonly TestLoggerProvider _loggerProvider;
        private readonly FunctionsSyncService _syncService;
        private readonly Mock<IScriptHostManager> _mockScriptHostManager;
        private readonly Mock<IPrimaryHostStateProvider> _mockPrimaryHostStateProviderMock;
        private readonly Mock<IFunctionsSyncManager> _mockSyncManager;
        private readonly Mock<IScriptWebHostEnvironment> _mockWebHostEnvironment;
        private readonly Mock<IEnvironment> _mockEnvironment;
        private readonly int _testDueTime = 250;

        public FunctionsSyncServiceTests()
        {
            _loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);

            _mockScriptHostManager = new Mock<IScriptHostManager>(MockBehavior.Strict);
            _mockPrimaryHostStateProviderMock = new Mock<IPrimaryHostStateProvider>(MockBehavior.Strict);
            _mockSyncManager = new Mock<IFunctionsSyncManager>(MockBehavior.Strict);

            _mockPrimaryHostStateProviderMock.Setup(p => p.IsPrimary).Returns(true);
            _mockScriptHostManager.Setup(p => p.State).Returns(ScriptHostState.Running);
            _mockSyncManager.Setup(p => p.TrySyncTriggersAsync(true)).ReturnsAsync(new SyncTriggersResult { Success = true });

            _mockWebHostEnvironment = new Mock<IScriptWebHostEnvironment>(MockBehavior.Strict);
            _mockWebHostEnvironment.SetupGet(p => p.InStandbyMode).Returns(false);
            _mockEnvironment = new Mock<IEnvironment>(MockBehavior.Strict);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady)).Returns("1");
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.CoreToolsEnvironment)).Returns((string)null);

            _syncService = new FunctionsSyncService(loggerFactory, _mockScriptHostManager.Object, _mockPrimaryHostStateProviderMock.Object, _mockSyncManager.Object);
            _syncService.DueTime = _testDueTime;
        }

        [Theory]
        [InlineData(true, ScriptHostState.Running, true)]
        [InlineData(false, ScriptHostState.Running, false)]
        [InlineData(true, ScriptHostState.Stopped, false)]
        [InlineData(true, ScriptHostState.Starting, false)]
        [InlineData(false, ScriptHostState.Stopped, false)]
        public void ShouldSyncTriggers_ReturnsExpectedResult(bool isPrimary, ScriptHostState hostState, bool expected)
        {
            _mockPrimaryHostStateProviderMock.Setup(p => p.IsPrimary).Returns(isPrimary);
            _mockScriptHostManager.Setup(p => p.State).Returns(hostState);

            Assert.Equal(expected, _syncService.ShouldSyncTriggers);
        }

        [Fact]
        public async Task StartAsync_PrimaryHost_Running_SyncsTriggers_AfterTimeout()
        {
            await _syncService.StartAsync(CancellationToken.None);
            await Task.Delay(2 * _testDueTime);

            _mockSyncManager.Verify(p => p.TrySyncTriggersAsync(true), Times.Once);

            var logMessage = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).Single();
            Assert.Equal("Initiating background SyncTriggers operation", logMessage);
        }

        [Fact]
        public async Task StartAsync_TokenCancelledBeforeTimeout_DoesNotSyncTriggers()
        {
            var cts = new CancellationTokenSource();
            cts.CancelAfter(_testDueTime / 2);
            await _syncService.StartAsync(cts.Token);
            await Task.Delay(2 * _testDueTime);

            _mockSyncManager.Verify(p => p.TrySyncTriggersAsync(true), Times.Never);
        }

        [Fact]
        public async Task StopCalledBeforeTimeout_DoesNotSyncTriggers()
        {
            await _syncService.StartAsync(CancellationToken.None);
            await _syncService.StopAsync(CancellationToken.None);
            await Task.Delay(2 * _testDueTime);

            _mockSyncManager.Verify(p => p.TrySyncTriggersAsync(true), Times.Never);
        }

        [Fact]
        public async Task UnhandledExceptions_HandledInCallback()
        {
            _mockSyncManager.Setup(p => p.TrySyncTriggersAsync(true)).ThrowsAsync(new Exception("Kaboom!"));

            await _syncService.StartAsync(CancellationToken.None);
            await Task.Delay(2 * _testDueTime);

            _mockSyncManager.Verify(p => p.TrySyncTriggersAsync(true), Times.Once);
        }

        [Theory]
        [InlineData(true, true, false, true, true, true, false, false)] // in standby
        [InlineData(true, true, false, false, true, true, false, false)] // in standby
        [InlineData(true, true, false, true, true, false, false, false)] // in standby
        [InlineData(true, true, false, false, true, false, true, false)] // in standby
        [InlineData(true, true, false, true, false, true, false, true)]
        [InlineData(true, true, false, false, false, true, false, true)]
        [InlineData(false, true, true, true, false, false, false, false)] // container not ready
        [InlineData(false, true, true, false, false, false, false, false)] // container not ready
        [InlineData(false, true, true, true, false, true, false, true)]
        [InlineData(false, true, true, false, false, true, false, true)]
        [InlineData(false, false, true, true, false, true, false, false)] // no encryption key
        [InlineData(false, false, true, false, false, true, false, false)] // no encryption key
        // note: normally linux dedicated would have AzureWebsiteInstanceId set.
        // However the test will always catch it as Windows because it checks the OS of the running process.
        // Setting AzureWebsiteInstanceId to null will make it fail the IsAppServiceWindows, and it shouldn't affect the check for dedicated linux
        [InlineData(false, true, false, true, false, false, false, false)] // dedicated linux
        [InlineData(false, true, false, false, false, false, false, true)] // dedicated linux
        [InlineData(false, true, false, false, false, false, true, true)] // managed app environment
        public void IsSyncTriggersEnvironment_StandbyMode_ReturnsExpectedResult(bool isAppService, bool hasEncryptionKey, bool isConsumptionLinuxOnAtlas, bool isConsumptionLinuxOnLegion, bool standbyMode, bool containerReady, bool isManagedAppEnvironment, bool expected)
        {
            _mockWebHostEnvironment.SetupGet(p => p.InStandbyMode).Returns(standbyMode);

            if (isConsumptionLinuxOnLegion)
            {
                _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku)).Returns(ScriptConstants.FlexConsumptionSku);
            }
            else
            {
                _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku)).Returns(ScriptConstants.DynamicSku);
            }

            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId)).Returns(isAppService ? "1" : null);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey)).Returns(hasEncryptionKey ? "1" : null);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.ContainerName)).Returns(isConsumptionLinuxOnAtlas || isConsumptionLinuxOnLegion ? "1" : null);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.LegionServiceHost)).Returns(isConsumptionLinuxOnLegion ? "1" : null);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady)).Returns(containerReady ? "1" : null);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.ManagedEnvironment)).Returns(isManagedAppEnvironment ? "1" : null);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.WebsitePodName)).Returns(isConsumptionLinuxOnLegion ? "RandomPodName" : null);

            var result = FunctionsSyncManager.IsSyncTriggersEnvironment(_mockWebHostEnvironment.Object, _mockEnvironment.Object);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void IsSyncTriggersEnvironment_LocalEnvironment_ReturnsExpectedResult(bool coreToolsEnvironment, bool expected)
        {
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.CoreToolsEnvironment)).Returns(coreToolsEnvironment ? "1" : null);

            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId)).Returns("1");
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey)).Returns("1");
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.ContainerName)).Returns((string)null);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady)).Returns("1");

            var result = FunctionsSyncManager.IsSyncTriggersEnvironment(_mockWebHostEnvironment.Object, _mockEnvironment.Object);
            Assert.Equal(expected, result);
        }
    }
}
