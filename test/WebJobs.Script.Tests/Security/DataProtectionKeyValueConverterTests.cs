// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

using static Microsoft.Azure.Web.DataProtection.Constants;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class DataProtectionKeyValueConverterTests
    {
        private ScriptSettingsManager _settingsManager = ScriptSettingsManager.Instance;

        [Fact]
        public void ReadKeyValue_CanRead_WrittenKey()
        {
            var converter = new DataProtectionKeyValueConverter(FileAccess.ReadWrite);

            string keyId = Guid.NewGuid().ToString();

            using (var variables = new TestScopedSettings(_settingsManager, AzureWebsiteLocalEncryptionKey, "0F75CA46E7EBDD39E4CA6B074D1F9A5972B849A55F91A248"))
            {
                // Create our test input key
                var testInputKey = new Key { Name = "Test", Value = "Test secret value" };

                // Encrypt the key
                var resultKey = converter.WriteValue(testInputKey);

                // Decrypt the encrypted key
                Key decryptedSecret = converter.ReadValue(resultKey);

                Assert.Equal(testInputKey.Value, decryptedSecret.Value);
            }
        }

        [Fact]
        public void WriteValue_WithReadAccess_ThrowsExpectedException()
        {
            var converter = new DataProtectionKeyValueConverter(FileAccess.Read);
            Assert.Throws<InvalidOperationException>(() => converter.WriteValue(new Key()));
        }

        [Fact]
        public void ReadValue_WithWriteAccess_ThrowsExpectedException()
        {
            var converter = new DataProtectionKeyValueConverter(FileAccess.Write);
            Assert.Throws<InvalidOperationException>(() => converter.ReadValue(new Key()));
        }

        /// <summary>
        /// Managed file secrets use the shared Azure key without changing hosting identity.
        /// </summary>
        [Fact]
        public async Task ManagedFiles_WithoutContainerName_PersistSharedEncryptedKeys()
        {
            using var variables = new TestScopedEnvironmentVariable(CreateManagedFileEnvironment(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))));
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            try
            {
                string originalMaster;
                using (var manager = CreateFileSecretManager(directory))
                {
                    originalMaster = (await manager.GetHostSecretsAsync()).MasterKey;
                }

                using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "host.json")));
                var master = document.RootElement.GetProperty("masterKey");
                Assert.True(master.GetProperty("encrypted").GetBoolean());
                var payload = WebEncoders.Base64UrlDecode(master.GetProperty("value").GetString());
                Assert.Equal(Guid.Empty, new Guid(payload.AsSpan(4, 16)));

                using var replacement = CreateFileSecretManager(directory);
                Assert.Equal(originalMaster, (await replacement.GetHostSecretsAsync()).MasterKey);
                Assert.Empty(Directory.GetFiles(directory, "*.snapshot.*.json"));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        /// <summary>
        /// Existing fixed-key ciphertext remains readable without regenerating keys.
        /// </summary>
        [Fact]
        public async Task ManagedFiles_ExistingAzureCiphertext_PreservesMasterKey()
        {
            using var variables = new TestScopedEnvironmentVariable(CreateManagedFileEnvironment(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))));
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(directory);
                var master = SecretGenerator.GenerateMasterKeyValue();
                var protector = Microsoft.Azure.Web.DataProtection.DataProtectionProvider
                    .CreateAzureDataProtector(configurationHandler: null, skipEnvironmentValidation: true)
                    .CreateProtector("function-secrets");
                var existing = new HostSecrets
                {
                    MasterKey = new Key("master", protector.Protect(master)) { IsEncrypted = true },
                    FunctionKeys = new List<Key>(),
                    SystemKeys = new List<Key>()
                };
                var path = Path.Combine(directory, "host.json");
                File.WriteAllText(path, ScriptSecretSerializer.SerializeSecrets(existing));
                var originalContents = File.ReadAllText(path);

                using var manager = CreateFileSecretManager(directory);
                Assert.Equal(master, (await manager.GetHostSecretsAsync()).MasterKey);
                Assert.Equal(originalContents, File.ReadAllText(path));
                Assert.Empty(Directory.GetFiles(directory, "*.snapshot.*.json"));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        /// <summary>
        /// Managed file storage rejects missing or invalid encryption material before persistence.
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-a-key")]
        [InlineData("0123456789ABCDEF")]
        [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
        public void ManagedFiles_InvalidEncryptionKey_FailsBeforeWriting(string encryptionKey)
        {
            using var variables = new TestScopedEnvironmentVariable(CreateManagedFileEnvironment(encryptionKey));
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            try
            {
                Assert.Throws<InvalidOperationException>(() => CreateFileSecretManager(directory));
                Assert.False(File.Exists(Path.Combine(directory, "host.json")));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        /// <summary>
        /// Managed non-Logic-App file storage retains its plaintext fallback without a key.
        /// </summary>
        [Fact]
        public async Task ManagedNonLogicAppFiles_WithoutEncryptionKey_KeepsExistingBehavior()
        {
            var environment = CreateManagedFileEnvironment(encryptionKey: null);
            environment[EnvironmentSettingNames.AppKind] = "functionapp";
            using var variables = new TestScopedEnvironmentVariable(environment);
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            try
            {
                using var manager = CreateFileSecretManager(directory);
                var secrets = await manager.GetHostSecretsAsync();
                using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "host.json")));
                var master = document.RootElement.GetProperty("masterKey");
                Assert.False(master.GetProperty("encrypted").GetBoolean());
                Assert.Equal(secrets.MasterKey, master.GetProperty("value").GetString());
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        /// <summary>
        /// Managed non-Logic-App file storage retains the default provider with a key.
        /// </summary>
        [Fact]
        public async Task ManagedNonLogicAppFiles_WithEncryptionKey_UsesExistingProvider()
        {
            var environment = CreateManagedFileEnvironment(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
            environment[EnvironmentSettingNames.AppKind] = "functionapp";
            using var variables = new TestScopedEnvironmentVariable(environment);
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            try
            {
                using var manager = CreateFileSecretManager(directory);
                await manager.GetHostSecretsAsync();
                using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "host.json")));
                var master = document.RootElement.GetProperty("masterKey");
                Assert.True(master.GetProperty("encrypted").GetBoolean());
                var payload = WebEncoders.Base64UrlDecode(master.GetProperty("value").GetString());
                Assert.NotEqual(Guid.Empty, new Guid(payload.AsSpan(4, 16)));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        /// <summary>
        /// Unmanaged file storage keeps its existing plaintext fallback when no key is configured.
        /// </summary>
        [Fact]
        public async Task UnmanagedFiles_WithoutEncryptionKey_KeepsExistingBehavior()
        {
            var environment = CreateManagedFileEnvironment(encryptionKey: null);
            environment[EnvironmentSettingNames.ManagedEnvironment] = null;
            using var variables = new TestScopedEnvironmentVariable(environment);
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            try
            {
                using var manager = CreateFileSecretManager(directory);
                var secrets = await manager.GetHostSecretsAsync();
                using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "host.json")));
                var master = document.RootElement.GetProperty("masterKey");
                Assert.False(master.GetProperty("encrypted").GetBoolean());
                Assert.Equal(secrets.MasterKey, master.GetProperty("value").GetString());
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        /// <summary>
        /// Non-file repositories do not acquire the managed file key requirement.
        /// </summary>
        /// <param name="storageType">The repository storage type.</param>
        [Theory]
        [InlineData("keyvault")]
        [InlineData("blob")]
        public void NonFileProviders_WithoutEncryptionKey_KeepExistingBehavior(string storageType)
        {
            using var variables = new TestScopedEnvironmentVariable(CreateManagedFileEnvironment(encryptionKey: null));
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            try
            {
                using var manager = CreateFileSecretManager(directory, storageType);
                Assert.NotNull(manager);
                Assert.False(File.Exists(Path.Combine(directory, "host.json")));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        /// <summary>
        /// Creates a process environment without alternate hosting or encryption-key markers.
        /// </summary>
        /// <param name="encryptionKey">The configured encryption key.</param>
        private static Dictionary<string, string> CreateManagedFileEnvironment(string encryptionKey)
        {
            return new Dictionary<string, string>
            {
                [EnvironmentSettingNames.ManagedEnvironment] = "true",
                [EnvironmentSettingNames.AppKind] = "workflowApp",
                [EnvironmentSettingNames.AzureWebJobsSecretStorageType] = "Files",
                [EnvironmentSettingNames.ContainerName] = null,
                [EnvironmentSettingNames.AzureWebsiteInstanceId] = null,
                [EnvironmentSettingNames.LegionServiceHost] = null,
                [AzureWebsiteLocalEncryptionKey] = encryptionKey,
                [AzureWebsiteEnvironmentMachineKey] = null
            };
        }

        /// <summary>
        /// Creates the real provider while isolating external storage and metrics dependencies.
        /// </summary>
        /// <param name="directory">The temporary secrets directory.</param>
        /// <param name="storageType">The repository storage type.</param>
        private static SecretManager CreateFileSecretManager(string directory, string storageType = "Files")
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageType, storageType);
            environment.SetEnvironmentVariable(EnvironmentSettingNames.ManagedEnvironment, Environment.GetEnvironmentVariable(EnvironmentSettingNames.ManagedEnvironment));
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AppKind, Environment.GetEnvironmentVariable(EnvironmentSettingNames.AppKind));
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName, "managed-files.example");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, "managed-files");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageKeyVaultUri, "https://isolated-vault.example");
            var options = new Mock<IOptionsMonitor<ScriptApplicationHostOptions>>();
            options.SetupGet(monitor => monitor.CurrentValue).Returns(new ScriptApplicationHostOptions { SecretsPath = directory });
            var storage = new Mock<IAzureBlobStorageProvider>();
            var container = new BlobContainerClient(new Uri("https://managed-files.example/azure-webjobs-secrets"));
            storage.Setup(provider => provider.TryCreateHostingBlobContainerClient(out container)).Returns(true);
            var provider = new DefaultSecretManagerProvider(
                options.Object,
                new Mock<IHostIdProvider>().Object,
                environment,
                NullLoggerFactory.Instance,
                new Mock<IMetricsLogger>().Object,
                new HostNameProvider(environment),
                new StartupContextProvider(environment, NullLogger<StartupContextProvider>.Instance),
                storage.Object);
            return (SecretManager)provider.Current;
        }
    }
}