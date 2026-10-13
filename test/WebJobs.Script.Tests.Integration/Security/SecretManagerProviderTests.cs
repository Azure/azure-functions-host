// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using WebJobs.Script.Tests;
using Xunit;

using static Microsoft.Azure.Web.DataProtection.Constants;

namespace Microsoft.Azure.WebJobs.Script.Tests.Security
{
    public class SecretManagerProviderTests
    {
        private const string InvalidManagedKeyMessage = "Managed file secret storage requires a 256-bit hexadecimal 'AzureWebEncryptionKey'.";
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
        public async Task Create_ManagedLogicAppFiles_UsesAzureKeyRepository()
        {
            using var variables = new TestScopedEnvironmentVariable(CreateManagedEnvironment(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))));
            using var directory = new TempDirectory();
            using var manager = (SecretManager)CreateSecretManagerProvider(directory.Path).Current;

            await manager.GetHostSecretsAsync();

            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.Path, "host.json")));
            var master = document.RootElement.GetProperty("masterKey");
            Assert.True(master.GetProperty("encrypted").GetBoolean());
            var payload = WebEncoders.Base64UrlDecode(master.GetProperty("value").GetString());
            Assert.Equal(Guid.Empty, new Guid(payload.AsSpan(4, 16)));
        }

        [Fact]
        public async Task Create_ManagedLogicAppFiles_ReplacementManagerPreservesKey()
        {
            using var variables = new TestScopedEnvironmentVariable(CreateManagedEnvironment(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))));
            using var directory = new TempDirectory();
            string originalMaster;
            using (var manager = (SecretManager)CreateSecretManagerProvider(directory.Path).Current)
            {
                originalMaster = (await manager.GetHostSecretsAsync()).MasterKey;
            }

            using var replacement = (SecretManager)CreateSecretManagerProvider(directory.Path).Current;
            Assert.Equal(originalMaster, (await replacement.GetHostSecretsAsync()).MasterKey);
            Assert.Empty(Directory.GetFiles(directory.Path, "*.snapshot.*.json"));
        }

        [Fact]
        public void CreateSecretsRepository_ManagedLogicAppFiles_KeySetAfterConstructionSucceeds()
        {
            using var variables = new TestScopedEnvironmentVariable(CreateManagedEnvironment(encryptionKey: null));
            using var directory = new TempDirectory();
            var provider = CreateSecretManagerProvider(directory.Path);

            ScriptSettingsManager.Instance.SetSetting(AzureWebsiteLocalEncryptionKey, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));

            using var repository = Assert.IsType<FileSystemSecretsRepository>(provider.CreateSecretsRepository());
        }

        [Fact]
        public void CreateSecretsRepository_ManagedLogicAppFiles_MissingKeyThrows()
        {
            using var variables = new TestScopedEnvironmentVariable(CreateManagedEnvironment(encryptionKey: null));
            using var directory = new TempDirectory();
            var provider = CreateSecretManagerProvider(directory.Path);

            var error = Assert.Throws<InvalidOperationException>(() => provider.CreateSecretsRepository());

            Assert.Equal(InvalidManagedKeyMessage, error.Message);
            Assert.False(File.Exists(Path.Combine(directory.Path, "host.json")));
        }

        [Theory]
        [InlineData(64, 'G')]
        [InlineData(62, 'A')]
        public void CreateSecretsRepository_ManagedLogicAppFiles_InvalidKeyThrows(int keyLength, char keyCharacter)
        {
            using var variables = new TestScopedEnvironmentVariable(CreateManagedEnvironment(new string(keyCharacter, keyLength)));
            using var directory = new TempDirectory();
            var provider = CreateSecretManagerProvider(directory.Path);

            var error = Assert.Throws<InvalidOperationException>(() => provider.CreateSecretsRepository());

            Assert.Equal(InvalidManagedKeyMessage, error.Message);
            Assert.False(File.Exists(Path.Combine(directory.Path, "host.json")));
        }

        [Fact]
        public async Task Create_ManagedNonLogicAppFiles_WithoutKeyPreservesPlaintext()
        {
            using var variables = new TestScopedEnvironmentVariable(CreateManagedEnvironment(encryptionKey: null, appKind: "functionapp"));
            using var directory = new TempDirectory();
            using var manager = (SecretManager)CreateSecretManagerProvider(directory.Path).Current;

            var secrets = await manager.GetHostSecretsAsync();

            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.Path, "host.json")));
            var master = document.RootElement.GetProperty("masterKey");
            Assert.False(master.GetProperty("encrypted").GetBoolean());
            Assert.Equal(secrets.MasterKey, master.GetProperty("value").GetString());
        }

        [Fact]
        public void CreateSecretsRepository_ManagedLogicAppBlob_WithoutKeySucceeds()
        {
            using var variables = new TestScopedEnvironmentVariable(CreateManagedEnvironment(encryptionKey: null, storageType: "Blob"));
            using var directory = new TempDirectory();
            var provider = CreateSecretManagerProvider(directory.Path);

            using var repository = Assert.IsType<BlobStorageSecretsRepository>(provider.CreateSecretsRepository());
            Assert.False(File.Exists(Path.Combine(directory.Path, "host.json")));
        }

        /// <summary>
        /// Creates a managed app environment without alternate hosting or encryption-key markers.
        /// </summary>
        /// <param name="encryptionKey">The configured encryption key.</param>
        /// <param name="appKind">The application kind.</param>
        /// <param name="storageType">The secret storage type.</param>
        private static Dictionary<string, string> CreateManagedEnvironment(string encryptionKey, string appKind = "workflowApp", string storageType = "Files")
        {
            return new Dictionary<string, string>
            {
                [EnvironmentSettingNames.ManagedEnvironment] = "true",
                [EnvironmentSettingNames.AppKind] = appKind,
                [EnvironmentSettingNames.AzureWebJobsSecretStorageType] = storageType,
                [EnvironmentSettingNames.AzureWebJobsSecretStorageSas] = null,
                [EnvironmentSettingNames.AzureWebsiteHostName] = "managed-files.example",
                [EnvironmentSettingNames.AzureWebsiteName] = "managed-files",
                [EnvironmentSettingNames.ContainerName] = null,
                [EnvironmentSettingNames.AzureWebsiteInstanceId] = null,
                [EnvironmentSettingNames.LegionServiceHost] = null,
                [AzureWebsiteLocalEncryptionKey] = encryptionKey,
                [AzureWebsiteEnvironmentMachineKey] = null
            };
        }

        /// <summary>
        /// Creates the real provider with isolated storage and metrics dependencies.
        /// </summary>
        /// <param name="directory">The temporary secrets directory.</param>
        private static DefaultSecretManagerProvider CreateSecretManagerProvider(string directory)
        {
            var environment = SystemEnvironment.Instance;
            var options = new Mock<IOptionsMonitor<ScriptApplicationHostOptions>>();
            options.SetupGet(monitor => monitor.CurrentValue).Returns(new ScriptApplicationHostOptions { SecretsPath = directory });
            var storage = new Mock<IAzureBlobStorageProvider>(MockBehavior.Strict);
            var container = new BlobContainerClient(new Uri("https://managed-files.example/azure-webjobs-secrets"));
            storage.Setup(provider => provider.TryCreateHostingBlobContainerClient(out container)).Returns(true);

            return new DefaultSecretManagerProvider(
                options.Object,
                new Mock<IHostIdProvider>().Object,
                environment,
                NullLoggerFactory.Instance,
                new TestMetricsLogger(),
                new HostNameProvider(environment),
                new StartupContextProvider(environment, NullLogger<StartupContextProvider>.Instance),
                storage.Object);
        }
    }
}
