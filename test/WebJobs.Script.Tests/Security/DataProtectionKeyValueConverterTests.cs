// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost;
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
        /// Managed Logic App file secrets use the shared Azure key without changing hosting identity.
        /// </summary>
        [Fact]
        public async Task ManagedLogicAppFiles_WithoutContainerName_UsesAzureKeyRepository()
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
        private static SecretManager CreateFileSecretManager(string directory)
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageType, "Files");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.ManagedEnvironment, Environment.GetEnvironmentVariable(EnvironmentSettingNames.ManagedEnvironment));
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AppKind, Environment.GetEnvironmentVariable(EnvironmentSettingNames.AppKind));
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName, "managed-files.example");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, "managed-files");
            var options = new Mock<IOptionsMonitor<ScriptApplicationHostOptions>>();
            options.SetupGet(monitor => monitor.CurrentValue).Returns(new ScriptApplicationHostOptions { SecretsPath = directory });
            var provider = new DefaultSecretManagerProvider(
                options.Object,
                new Mock<IHostIdProvider>().Object,
                environment,
                NullLoggerFactory.Instance,
                new Mock<IMetricsLogger>().Object,
                new HostNameProvider(environment),
                new StartupContextProvider(environment, NullLogger<StartupContextProvider>.Instance),
                new Mock<IAzureBlobStorageProvider>(MockBehavior.Strict).Object);
            return (SecretManager)provider.Current;
        }
    }
}