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
    /// Tests for the Flex triggers store notification in <see cref="FunctionsSyncManager"/>: the platform's report is
    /// returned as is, and a failure to notify is reported as <see cref="PlatformTriggersStoreStatus.NotConfirmed"/>.
    /// </summary>
    public class FunctionsSyncManagerPlatformNotificationTests
    {
        [Theory]
        [InlineData(PlatformTriggersStoreStatus.Stored)]
        [InlineData(PlatformTriggersStoreStatus.Failed)]
        public async Task NotifyPlatformTriggersStoreAsync_ReturnsPlatformStatus(PlatformTriggersStoreStatus status)
        {
            var mockMesh = new Mock<IMeshServiceClient>(MockBehavior.Strict);
            mockMesh.Setup(m => m.NotifyTriggersChanged()).ReturnsAsync(status);

            var syncManager = CreateSyncManager(mockMesh.Object);

            Assert.Equal(status, await syncManager.NotifyPlatformTriggersStoreAsync());
            mockMesh.Verify(m => m.NotifyTriggersChanged(), Times.Once);
        }

        [Fact]
        public async Task NotifyPlatformTriggersStoreAsync_MeshThrows_ReturnsNotConfirmed()
        {
            var mockMesh = new Mock<IMeshServiceClient>(MockBehavior.Strict);
            mockMesh.Setup(m => m.NotifyTriggersChanged())
                .ThrowsAsync(new HttpRequestException("simulated mesh failure"));

            var syncManager = CreateSyncManager(mockMesh.Object);

            Assert.Equal(PlatformTriggersStoreStatus.NotConfirmed, await syncManager.NotifyPlatformTriggersStoreAsync());
        }

        private static FunctionsSyncManager CreateSyncManager(IMeshServiceClient meshServiceClient)
        {
            var environmentMock = new Mock<IEnvironment>();

            var appHostOptions = new Mock<IOptionsMonitor<ScriptApplicationHostOptions>>();
            appHostOptions.SetupGet(p => p.CurrentValue).Returns(new ScriptApplicationHostOptions { ScriptPath = "/dev/null" });

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
                Mock.Of<IScriptHostManager>(),
                meshServiceClient);
        }
    }
}
