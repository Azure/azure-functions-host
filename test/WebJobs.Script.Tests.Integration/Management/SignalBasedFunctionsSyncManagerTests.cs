// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Managment
{
    public class SignalBasedFunctionsSyncManagerTests
    {
        private readonly Dictionary<string, string> _vars;
        private readonly Mock<IMeshServiceClient> _mockMeshServiceClient;
        private readonly Mock<IScriptWebHostEnvironment> _mockWebHostEnvironment;
        private readonly Mock<IEnvironment> _mockEnvironment;
        private readonly Mock<IFunctionMetadataManager> _mockFunctionMetadataManager;
        private readonly Mock<IHostIdProvider> _mockHostIdProvider;
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _appHostOptions;
        private readonly IOptions<FunctionsHostingConfigOptions> _hostingConfigOptions;
        private readonly SignalBasedFunctionsSyncManager _signalSyncManager;

        public SignalBasedFunctionsSyncManagerTests()
        {
            _vars = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.WebSiteAuthEncryptionKey, TestHelpers.GenerateKeyHexString() },
                { EnvironmentSettingNames.AzureWebsiteHostName, "appName.azurewebsites.net" }
            };

            _mockWebHostEnvironment = new Mock<IScriptWebHostEnvironment>(MockBehavior.Strict);
            _mockWebHostEnvironment.SetupGet(p => p.InStandbyMode).Returns(false);

            _mockEnvironment = new Mock<IEnvironment>();
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(It.IsAny<string>())).Returns((string)null);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey)).Returns("1");
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady)).Returns("1");
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName)).Returns("appName.azurewebsites.net");
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku)).Returns(ScriptConstants.FlexConsumptionSku);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteArmCacheEnabled)).Returns("0");

            _mockMeshServiceClient = new Mock<IMeshServiceClient>();

            var metadata = new FunctionMetadata
            {
                Name = "TestFunction",
                ScriptFile = "file1.csx"
            };
            metadata.Bindings.Add(new BindingMetadata
            {
                Name = "req",
                Type = "httpTrigger",
                Direction = BindingDirection.In,
                Raw = new JObject
                {
                    { "authLevel", "function" },
                    { "type", "httpTrigger" },
                    { "direction", "in" },
                    { "name", "req" }
                }
            });

            _mockFunctionMetadataManager = new Mock<IFunctionMetadataManager>();
            _mockFunctionMetadataManager.Setup(p => p.GetFunctionMetadata(It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns(ImmutableArray.Create(metadata));

            _mockHostIdProvider = new Mock<IHostIdProvider>();
            _mockHostIdProvider.Setup(p => p.GetHostIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync("testhostid");

            var mockAppHostOptions = new Mock<IOptionsMonitor<ScriptApplicationHostOptions>>();
            mockAppHostOptions.Setup(p => p.CurrentValue).Returns(new ScriptApplicationHostOptions
            {
                ScriptPath = "somePath"
            });
            _appHostOptions = mockAppHostOptions.Object;

            var mockHostingConfigOptions = new Mock<IOptions<FunctionsHostingConfigOptions>>();
            mockHostingConfigOptions.Setup(p => p.Value).Returns(new FunctionsHostingConfigOptions());
            _hostingConfigOptions = mockHostingConfigOptions.Object;

            var configuration = ScriptSettingsManager.BuildDefaultConfiguration();
            var azureBlobStorageProvider = TestHelpers.GetAzureBlobStorageProvider(configuration);

            _signalSyncManager = new SignalBasedFunctionsSyncManager(
                _mockHostIdProvider.Object,
                _appHostOptions,
                NullLogger<SignalBasedFunctionsSyncManager>.Instance,
                Mock.Of<IHttpClientFactory>(),
                Mock.Of<ISecretManagerProvider>(),
                _mockWebHostEnvironment.Object,
                _mockEnvironment.Object,
                new HostNameProvider(_mockEnvironment.Object),
                _mockFunctionMetadataManager.Object,
                azureBlobStorageProvider,
                _hostingConfigOptions,
                Mock.Of<IScriptHostManager>(),
                _mockMeshServiceClient.Object);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task TrySyncTriggers_NotifiesWithContentHash(bool isBackgroundSync)
        {
            using (var env = new TestScopedEnvironmentVariable(_vars))
            {
                var syncResult = await _signalSyncManager.TrySyncTriggersAsync(isBackgroundSync);

                Assert.True(syncResult.Success, syncResult.Error);
                Assert.True(string.IsNullOrEmpty(syncResult.Error), "Error should be null or empty");

                _mockMeshServiceClient.Verify(
                    m => m.NotifyTriggersChanged(It.IsAny<string>()),
                    Times.Once);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task TrySyncTriggers_NotifyFails_ReturnsError(bool isBackgroundSync)
        {
            _mockMeshServiceClient.Setup(m => m.NotifyTriggersChanged(It.IsAny<string>()))
                .ThrowsAsync(new Exception("Connection refused"));

            using (var env = new TestScopedEnvironmentVariable(_vars))
            {
                var syncResult = await _signalSyncManager.TrySyncTriggersAsync(isBackgroundSync);

                Assert.False(syncResult.Success);
                Assert.Equal("Failed to notify triggers changed.", syncResult.Error);
            }
        }
    }
}
