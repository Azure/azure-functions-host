// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Security
{
    public class SecretManagerProviderTests
    {
        private readonly ScriptApplicationHostOptions _options;
        private readonly TestChangeTokenSource<ScriptApplicationHostOptions> _tokenSource;
        private readonly DefaultSecretManagerProvider _provider;

        public SecretManagerProviderTests()
        {
            var mockIdProvider = new Mock<IHostIdProvider>();

            _options = new ScriptApplicationHostOptions
            {
                SecretsPath = Path.Combine("c:", "path1")
            };
            var factory = new TestOptionsFactory<ScriptApplicationHostOptions>(_options);
            _tokenSource = new TestChangeTokenSource<ScriptApplicationHostOptions>();
            var changeTokens = new[] { _tokenSource };
            var optionsMonitor = new OptionsMonitor<ScriptApplicationHostOptions>(factory, changeTokens, factory);

            var config = TestHelpers.GetTestConfiguration();

            mockIdProvider.Setup(p => p.GetHostIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync("testhostid");

            IEnvironment environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName, "test.azurewebsites.net");
            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);
            var hostNameProvider = new HostNameProvider(environment);
            var azureBlobStorageProvider = TestHelpers.GetAzureBlobStorageProvider(config);

            _provider = new DefaultSecretManagerProvider(optionsMonitor, mockIdProvider.Object, new TestEnvironment(), NullLoggerFactory.Instance,
                new TestMetricsLogger(), hostNameProvider, new StartupContextProvider(environment, loggerFactory.CreateLogger<StartupContextProvider>()), azureBlobStorageProvider);
        }

        [Fact]
        public void OptionsMonitor_OnChange_ResetsCurrent()
        {
            var manager1 = _provider.Current;
            var manager2 = _provider.Current;
            _tokenSource.SignalChange();
            var manager3 = _provider.Current;

            Assert.Same(manager1, manager2);
            Assert.NotSame(manager1, manager3);
        }

        [Fact]
        public void TryGetSecretsRepositoryType_ReturnsExpectedValue()
        {
            bool result = _provider.TryGetSecretsRepositoryType(out Type repositoryType);
            Assert.True(result);
            Assert.Equal(typeof(BlobStorageSecretsRepository), repositoryType);
        }

        [Fact]
        public void SecretsEnabled_ReturnsExpectedValue()
        {
            Assert.True(_provider.SecretsEnabled);

            // we'll return a cached value here
            Assert.True(_provider.SecretsEnabled);

            // force creation of the manager
            Assert.NotNull(_provider.Current);

            // will short circuit here
            Assert.True(_provider.SecretsEnabled);
        }

        [Fact]
        public void SecretsEnabled_InitialFailure_RecoversOnSubsequentCall()
        {
            // Simulate the startup race described in Azure/azure-functions-host#11787, where
            // TryCreateHostingBlobContainerClient initially fails because the script host's
            // IConfiguration has not yet been merged into HostAzureBlobStorageProvider via
            // ActiveHostConfigurationSource, and then succeeds once the active host configuration
            // becomes available. The provider must not cache the negative result, otherwise
            // key-based auth would remain disabled for the lifetime of the host even after the
            // configuration becomes valid.
            var mockIdProvider = new Mock<IHostIdProvider>();
            mockIdProvider.Setup(p => p.GetHostIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync("testhostid");

            var options = new ScriptApplicationHostOptions
            {
                SecretsPath = Path.Combine("c:", "path1")
            };
            var factory = new TestOptionsFactory<ScriptApplicationHostOptions>(options);
            var tokenSource = new TestChangeTokenSource<ScriptApplicationHostOptions>();
            var optionsMonitor = new OptionsMonitor<ScriptApplicationHostOptions>(factory, new[] { tokenSource }, factory);

            IEnvironment environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName, "test.azurewebsites.net");
            var hostNameProvider = new HostNameProvider(environment);

            BlobContainerClient containerClient = new BlobContainerClient(new Uri("https://example.blob.core.windows.net/azure-webjobs-hosts"));
            bool shouldSucceed = false;
            var mockBlobStorageProvider = new Mock<IAzureBlobStorageProvider>();
            mockBlobStorageProvider
                .Setup(p => p.TryCreateHostingBlobContainerClient(out containerClient))
                .Returns(() => shouldSucceed);

            var provider = new DefaultSecretManagerProvider(optionsMonitor, mockIdProvider.Object, environment, NullLoggerFactory.Instance,
                new TestMetricsLogger(), hostNameProvider, new StartupContextProvider(environment, NullLoggerFactory.Instance.CreateLogger<StartupContextProvider>()),
                mockBlobStorageProvider.Object);

            Assert.False(provider.SecretsEnabled);
            Assert.False(provider.SecretsEnabled);

            // The active host configuration becomes available.
            shouldSucceed = true;

            Assert.True(provider.SecretsEnabled);

            // Verify TryCreateHostingBlobContainerClient was probed for each call until it succeeded.
            mockBlobStorageProvider.Verify(p => p.TryCreateHostingBlobContainerClient(out containerClient), Times.Exactly(3));

            // After a successful result, subsequent calls should short-circuit and not re-probe.
            Assert.True(provider.SecretsEnabled);
            mockBlobStorageProvider.Verify(p => p.TryCreateHostingBlobContainerClient(out containerClient), Times.Exactly(3));
        }
    }
}
