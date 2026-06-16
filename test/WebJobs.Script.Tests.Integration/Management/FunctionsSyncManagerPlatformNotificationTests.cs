// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Management
{
    /// <summary>
    /// Tests for the best-effort platform notification in <see cref="FunctionsSyncManager"/>:
    /// when <see cref="EnvironmentSettingNames.FunctionsNotifyPlatformOnSync"/> is enabled, a
    /// mesh notification is fired after a successful front-end sync. Failures must not propagate.
    /// </summary>
    public class FunctionsSyncManagerPlatformNotificationTests
    {
        [Fact]
        public async Task NotifyPlatformIfEnabledAsync_Enabled_CallsMesh()
        {
            var mockMesh = new Mock<IMeshServiceClient>(MockBehavior.Strict);
            mockMesh.Setup(m => m.NotifyTriggersChanged()).Returns(Task.CompletedTask);

            var syncManager = CreateSyncManager(notifyPlatformOnSync: true, mockMesh.Object);

            await syncManager.NotifyPlatformIfEnabledAsync();

            mockMesh.Verify(m => m.NotifyTriggersChanged(), Times.Once);
        }

        [Fact]
        public async Task NotifyPlatformIfEnabledAsync_Disabled_DoesNotCallMesh()
        {
            var mockMesh = new Mock<IMeshServiceClient>(MockBehavior.Strict);

            var syncManager = CreateSyncManager(notifyPlatformOnSync: false, mockMesh.Object);

            await syncManager.NotifyPlatformIfEnabledAsync();

            mockMesh.Verify(m => m.NotifyTriggersChanged(), Times.Never);
        }

        [Fact]
        public async Task NotifyPlatformIfEnabledAsync_MeshThrows_DoesNotPropagate()
        {
            var mockMesh = new Mock<IMeshServiceClient>(MockBehavior.Strict);
            mockMesh.Setup(m => m.NotifyTriggersChanged())
                .ThrowsAsync(new HttpRequestException("simulated mesh failure"));

            var syncManager = CreateSyncManager(notifyPlatformOnSync: true, mockMesh.Object);

            // Should NOT throw; failure must be swallowed so the front-end result is preserved.
            await syncManager.NotifyPlatformIfEnabledAsync();

            mockMesh.Verify(m => m.NotifyTriggersChanged(), Times.Once);
        }

        private static FunctionsSyncManager CreateSyncManager(bool notifyPlatformOnSync, IMeshServiceClient meshServiceClient)
        {
            var environmentMock = new Mock<IEnvironment>();
            environmentMock
                .Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsNotifyPlatformOnSync))
                .Returns(notifyPlatformOnSync.ToString());

            var appHostOptions = new Mock<IOptionsMonitor<ScriptApplicationHostOptions>>();
            appHostOptions.SetupGet(p => p.CurrentValue).Returns(new ScriptApplicationHostOptions { ScriptPath = "/dev/null" });

            var hostingConfigOptions = new Mock<IOptions<FunctionsHostingConfigOptions>>();
            hostingConfigOptions.SetupGet(p => p.Value).Returns(new FunctionsHostingConfigOptions());

            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

            return new FunctionsSyncManager(
                Mock.Of<IHostIdProvider>(),
                appHostOptions.Object,
                NullLogger<FunctionsSyncManager>.Instance,
                httpClientFactory.Object,
                Mock.Of<ISecretManagerProvider>(),
                Mock.Of<IScriptWebHostEnvironment>(),
                environmentMock.Object,
                new HostNameProvider(environmentMock.Object),
                Mock.Of<IFunctionMetadataManager>(),
                Mock.Of<IAzureBlobStorageProvider>(),
                hostingConfigOptions.Object,
                Mock.Of<IScriptHostManager>(),
                meshServiceClient);
        }
    }
}
