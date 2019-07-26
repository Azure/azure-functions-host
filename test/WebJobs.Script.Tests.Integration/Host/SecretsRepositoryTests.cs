// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class SecretsRepositoryTests : IClassFixture<SecretsRepositoryTests.Fixture>
    {
        private readonly SecretsRepositoryTests.Fixture _fixture;
        private readonly string functionName = "Test_test";
        public static string KeyName = "Te!@#st!1-te_st";

        public SecretsRepositoryTests(SecretsRepositoryTests.Fixture fixture)
        {
            _fixture = fixture;
        }

        public enum SecretsRepositoryType
        {
            FileSystem,
            BlobStorage,
            BlobStorageSas,
            KeyVault
        }

        [Fact]
        public async Task FileSystemRepo_Constructor_CreatesSecretPathIfNotExists()
        {
            await Constructor_CreatesSecretPathIfNotExists(SecretsRepositoryType.FileSystem);
        }

        [Theory]
        [InlineData(SecretsRepositoryType.BlobStorage)]
        [InlineData(SecretsRepositoryType.BlobStorageSas)]
        public async Task BlobStorageRepo_Constructor_CreatesSecretPathIfNotExists(SecretsRepositoryType repositoryType)
        {
            await Constructor_CreatesSecretPathIfNotExists(repositoryType);
        }

        [Fact]
        public async Task KeyVaultStorageeRepo_Constructor_CreatesSecretPathIfNotExists()
        {
            await Constructor_CreatesSecretPathIfNotExists(SecretsRepositoryType.KeyVault);
        }

        private async Task Constructor_CreatesSecretPathIfNotExists(SecretsRepositoryType repositoryType)
        {
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            await _fixture.TestInitialize(repositoryType, path);
            try
            {
                bool preConstDirExists = Directory.Exists(path);
                var target = _fixture.GetNewSecretRepository();
                bool postConstDirExists = Directory.Exists(path);

                Assert.False(preConstDirExists);
                Assert.True(postConstDirExists);
            }
            finally
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path);
                }
            }
        }

        [Theory]
        [InlineData(SecretsRepositoryType.BlobStorage, ScriptSecretsType.Host)]
        [InlineData(SecretsRepositoryType.BlobStorage, ScriptSecretsType.Function)]
        [InlineData(SecretsRepositoryType.BlobStorageSas, ScriptSecretsType.Host)]
        [InlineData(SecretsRepositoryType.BlobStorageSas, ScriptSecretsType.Function)]
        public async Task BlobStorageRepo_ReadAsync_ReadsExpectedFile(SecretsRepositoryType repositoryType, ScriptSecretsType secretsType)
        {
            await ReadAsync_ReadsExpectedFile(repositoryType, secretsType);
        }

        [Theory]
        [InlineData(ScriptSecretsType.Host)]
        [InlineData(ScriptSecretsType.Function)]
        public async Task FileSystemRepo_ReadAsync_ReadsExpectedFile(ScriptSecretsType secretsType)
        {
            await ReadAsync_ReadsExpectedFile(SecretsRepositoryType.FileSystem, secretsType);
        }

        [Theory]
        [InlineData(ScriptSecretsType.Host)]
        [InlineData(ScriptSecretsType.Function)]
        public async Task KeyVaultRepo_ReadAsync_ReadsExpectedFile(ScriptSecretsType secretsType)
        {
            await ReadAsync_ReadsExpectedFile(SecretsRepositoryType.KeyVault, secretsType);
        }

        private async Task ReadAsync_ReadsExpectedFile(SecretsRepositoryType repositoryType, ScriptSecretsType secretsType)
        {
            using (var directory = new TempDirectory())
            {
                await _fixture.TestInitialize(repositoryType, directory.Path);
                ScriptSecrets testSecrets = null;
                if (secretsType == ScriptSecretsType.Host)
                {
                    testSecrets = new HostSecrets()
                    {
                        MasterKey = new Key("master", "test"),
                        FunctionKeys = new List<Key>() {new Key(KeyName, "test")},
                        SystemKeys = new List<Key>() { new Key(KeyName, "test") }
                    };
                }
                else
                {
                    testSecrets = new FunctionSecrets()
                    {
                        Keys = new List<Key>() { new Key(KeyName, "test") }
                    };
                }
                string testFunctionName = secretsType == ScriptSecretsType.Host ? "host" : functionName;

                await _fixture.WriteSecret(testFunctionName, testSecrets);

                var target = _fixture.GetNewSecretRepository();

                ScriptSecrets secretsContent = await target.ReadAsync(secretsType, testFunctionName);

                if (secretsType == ScriptSecretsType.Host)
                {
                    Assert.Equal((secretsContent as HostSecrets).MasterKey.Name, "master");
                    Assert.Equal((secretsContent as HostSecrets).MasterKey.Value, "test");
                    Assert.Equal((secretsContent as HostSecrets).FunctionKeys[0].Name, KeyName);
                    Assert.Equal((secretsContent as HostSecrets).FunctionKeys[0].Value, "test");
                    Assert.Equal((secretsContent as HostSecrets).SystemKeys[0].Name, KeyName);
                    Assert.Equal((secretsContent as HostSecrets).SystemKeys[0].Value, "test");
                }
                else
                {
                    Assert.Equal((secretsContent as FunctionSecrets).Keys[0].Name, KeyName);
                    Assert.Equal((secretsContent as FunctionSecrets).Keys[0].Value, "test");
                }

            }
        }

        [Theory]
        [InlineData(SecretsRepositoryType.BlobStorage, ScriptSecretsType.Host)]
        [InlineData(SecretsRepositoryType.BlobStorage, ScriptSecretsType.Function)]
        [InlineData(SecretsRepositoryType.BlobStorageSas, ScriptSecretsType.Host)]
        [InlineData(SecretsRepositoryType.BlobStorageSas, ScriptSecretsType.Function)]
        public async Task BlobStorageRepo_WriteAsync_CreatesExpectedFile(SecretsRepositoryType repositoryType, ScriptSecretsType secretsType)
        {
            await WriteAsync_CreatesExpectedFile(repositoryType, secretsType);
        }

        [Theory]
        [InlineData(ScriptSecretsType.Host)]
        [InlineData(ScriptSecretsType.Function)]
        public async Task FileSystemRepo_WriteAsync_CreatesExpectedFile(ScriptSecretsType secretsType)
        {
            await WriteAsync_CreatesExpectedFile(SecretsRepositoryType.FileSystem, secretsType);
        }

        [Theory]
        [InlineData(ScriptSecretsType.Host)]
        [InlineData(ScriptSecretsType.Function)]
        public async Task KeyVaultRepo_WriteAsync_CreatesExpectedFile(ScriptSecretsType secretsType)
        {
            await WriteAsync_CreatesExpectedFile(SecretsRepositoryType.KeyVault, secretsType);
        }

        private async Task WriteAsync_CreatesExpectedFile(SecretsRepositoryType repositoryType, ScriptSecretsType secretsType)
        {
            using (var directory = new TempDirectory())
            {
                await _fixture.TestInitialize(repositoryType, directory.Path);
                ScriptSecrets secrets = null;
                if (secretsType == ScriptSecretsType.Host)
                {
                    secrets = new HostSecrets()
                    {
                        MasterKey = new Key("master", "test"),
                        FunctionKeys = new List<Key>() { new Key(KeyName, "test") },
                        SystemKeys = new List<Key>() { new Key(KeyName, "test") }
                    };
                }
                else
                {
                    secrets = new FunctionSecrets()
                    {
                        Keys = new List<Key>() { new Key(KeyName, "test") }
                    };
                }
                string testFunctionName = secretsType == ScriptSecretsType.Host ? null : functionName;

                var target = _fixture.GetNewSecretRepository();
                await target.WriteAsync(secretsType, testFunctionName, secrets);

                string filePath = Path.Combine(directory.Path, $"{testFunctionName ?? "host"}.json");

                if (repositoryType == SecretsRepositoryType.BlobStorage || repositoryType == SecretsRepositoryType.BlobStorageSas)
                {
                    Assert.True(_fixture.MarkerFileExists(testFunctionName ?? "host"));
                }

                ScriptSecrets secrets1 = await _fixture.GetSecretText(testFunctionName ?? "host", secretsType);
                if (secretsType == ScriptSecretsType.Host)
                {

                    Assert.Equal((secrets1 as HostSecrets).MasterKey.Name, "master");
                    Assert.Equal((secrets1 as HostSecrets).MasterKey.Value, "test");
                    Assert.Equal((secrets1 as HostSecrets).FunctionKeys[0].Name, KeyName);
                    Assert.Equal((secrets1 as HostSecrets).FunctionKeys[0].Value, "test");
                    Assert.Equal((secrets1 as HostSecrets).SystemKeys[0].Name, KeyName);
                    Assert.Equal((secrets1 as HostSecrets).SystemKeys[0].Value, "test");
                }
                else
                {
                    Assert.Equal((secrets1 as FunctionSecrets).Keys[0].Name, KeyName);
                    Assert.Equal((secrets1 as FunctionSecrets).Keys[0].Value, "test");
                }

            }
        }

        [Fact]
        public async Task FileSystemRepo_WriteAsync_ChangeNotificationUpdatesExistingSecret()
        {
            await WriteAsync_ChangeNotificationUpdatesExistingSecret(SecretsRepositoryType.FileSystem);
        }

        [Theory]
        [InlineData(SecretsRepositoryType.BlobStorage)]
        [InlineData(SecretsRepositoryType.BlobStorageSas)]
        public async Task BlobStorageRepo_WriteAsync_ChangeNotificationUpdatesExistingSecret(SecretsRepositoryType repositoryType)
        {
            await WriteAsync_ChangeNotificationUpdatesExistingSecret(repositoryType);
        }

        private async Task WriteAsync_ChangeNotificationUpdatesExistingSecret(SecretsRepositoryType repositoryType)
        {
            using (var directory = new TempDirectory())
            {
                await _fixture.TestInitialize(repositoryType, directory.Path);

                string testFunctionName = functionName;
                ScriptSecretsType secretsType = ScriptSecretsType.Function;
                FunctionSecrets initialSecretText = new FunctionSecrets()
                {
                    Keys = new List<Key> { new Key(KeyName, "test1") }
                };
                FunctionSecrets updatedSecretText = new FunctionSecrets()
                {
                    Keys = new List<Key> { new Key(KeyName, "test2") }
                };

                await _fixture.WriteSecret(testFunctionName, initialSecretText);
                var target = _fixture.GetNewSecretRepository();
                ScriptSecrets preTextResult = await target.ReadAsync(secretsType, testFunctionName);
                await _fixture.WriteSecret(testFunctionName, updatedSecretText);
                ScriptSecrets postTextResult = await target.ReadAsync(secretsType, testFunctionName);

                Assert.Equal("test1", (preTextResult as FunctionSecrets).Keys[0].Value);
                Assert.Equal("test2", (postTextResult as FunctionSecrets).Keys[0].Value);
            }
        }

        [Fact] // This test only run for FileSystemRepository as secrets purging is a no-op for blob storage secrets
        public async Task FileSystemRepo_PurgeOldSecrets_RemovesOldAndKeepsCurrentSecrets()
        {
            using (var directory = new TempDirectory())
            {
                await _fixture.TestInitialize(SecretsRepositoryType.FileSystem, directory.Path);
                Func<int, string> getFilePath = i => Path.Combine(directory.Path, $"{i}.json");

                var sequence = Enumerable.Range(0, 10);
                var files = sequence.Select(i => getFilePath(i)).ToList();

                // Create files
                files.ForEach(f => File.WriteAllText(f, "test"));

                var target = _fixture.GetNewSecretRepository();

                // Purge, passing even named files as the existing functions
                var currentFunctions = sequence.Where(i => i % 2 == 0).Select(i => i.ToString()).ToList();

                await target.PurgeOldSecretsAsync(currentFunctions, NullLogger.Instance);

                // Ensure only expected files exist
                Assert.True(sequence.All(i => (i % 2 == 0) == File.Exists(getFilePath(i))));
            }
        }

        [Theory]
        [InlineData(SecretsRepositoryType.FileSystem, ScriptSecretsType.Host)]
        [InlineData(SecretsRepositoryType.FileSystem, ScriptSecretsType.Function)]
        [InlineData(SecretsRepositoryType.BlobStorage, ScriptSecretsType.Host)]
        [InlineData(SecretsRepositoryType.BlobStorage, ScriptSecretsType.Function)]
        [InlineData(SecretsRepositoryType.BlobStorageSas, ScriptSecretsType.Host)]
        [InlineData(SecretsRepositoryType.BlobStorageSas, ScriptSecretsType.Function)]
        public async Task GetSecretSnapshots_ReturnsExpected(SecretsRepositoryType repositoryType, ScriptSecretsType secretsType)
        {
            using (var directory = new TempDirectory())
            {
                await _fixture.TestInitialize(repositoryType, directory.Path);
                ScriptSecrets secrets = null;
                if (secretsType == ScriptSecretsType.Host)
                {
                    secrets = new HostSecrets()
                    {
                        MasterKey = new Key("master", "test")
                    };
                }
                else
                {
                    secrets = new FunctionSecrets()
                    {
                        Keys = new List<Key>() { new Key(KeyName, "test") }
                    };
                }
                string testFunctionName = secretsType == ScriptSecretsType.Host ? null : functionName;

                var target = _fixture.GetNewSecretRepository();
                await target.WriteAsync(secretsType, testFunctionName, secrets);
                for (int i = 0; i < 5; i++)
                {
                    await target.WriteSnapshotAsync(secretsType, testFunctionName, secrets);
                }
                string[] files = await target.GetSecretSnapshots(secretsType, testFunctionName);

                Assert.True(files.Length > 0);
            }
        }

        public class Fixture : IDisposable
        {
            public Fixture()
            {
                TestSiteName = "Test_test";
                var configuration = TestHelpers.GetTestConfiguration();
                BlobConnectionString = configuration.GetWebJobsConnectionString(ConnectionStringNames.Storage);
                KeyVaultConnectionString = configuration.GetWebJobsConnectionString(EnvironmentSettingNames.AzureWebJobsSecretStorageKeyVaultConnectionString);
                KeyVaultName = configuration.GetWebJobsConnectionString(EnvironmentSettingNames.AzureWebJobsSecretStorageKeyVaultName);
                AzureServiceTokenProvider azureServiceTokenProvider = new AzureServiceTokenProvider(KeyVaultConnectionString);
                KeyVaultClient = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(azureServiceTokenProvider.KeyVaultTokenCallback));
            }

            public string TestSiteName { get; private set; }

            public string SecretsDirectory { get; private set; }

            public string BlobConnectionString { get; private set; }

            public Uri BlobSasConnectionUri { get; private set; }

            public CloudBlobContainer BlobContainer { get; private set; }

            public KeyVaultClient KeyVaultClient { get; private set; }

            public string KeyVaultName { get; private set; }

            public string KeyVaultConnectionString { get; private set; }

            public SecretsRepositoryType RepositoryType { get; private set; }

            public async Task TestInitialize(SecretsRepositoryType repositoryType, string secretsDirectory, string testSiteName = null)
            {
                RepositoryType = repositoryType;
                SecretsDirectory = secretsDirectory;
                if (testSiteName != null)
                {
                    TestSiteName = testSiteName;
                }

                if (RepositoryType == SecretsRepositoryType.BlobStorageSas)
                {
                    BlobSasConnectionUri = await TestHelpers.CreateBlobContainerSas(BlobConnectionString, "azure-webjobs-secrets-sas");
                    BlobContainer = new CloudBlobContainer(BlobSasConnectionUri);
                }
                else
                {
                    BlobContainer = CloudStorageAccount.Parse(BlobConnectionString).CreateCloudBlobClient().GetContainerReference("azure-webjobs-secrets");
                }

                await ClearAllBlobSecrets();
                ClearAllFileSecrets();
                await ClearAllKeyVaultSecrets();
            }

            public ISecretsRepository GetNewSecretRepository()
            {
                if (RepositoryType == SecretsRepositoryType.BlobStorage)
                {
                    return new BlobStorageSecretsRepository(SecretsDirectory, BlobConnectionString, TestSiteName);
                }
                else if (RepositoryType == SecretsRepositoryType.BlobStorageSas)
                {
                    return new BlobStorageSasSecretsRepository(SecretsDirectory, BlobSasConnectionUri.ToString(), TestSiteName);
                }
                else if (RepositoryType == SecretsRepositoryType.FileSystem)
                {
                    return new FileSystemSecretsRepository(SecretsDirectory);
                }
                else
                {
                    return new KeyVaultSecretsRepository(SecretsDirectory, KeyVaultName, KeyVaultConnectionString);
                }
            }

            public void Dispose()
            {
                try
                {
                    // delete blob files
                    ClearAllBlobSecrets().ContinueWith(t => { });
                    ClearAllFileSecrets();
                    ClearAllKeyVaultSecrets().ContinueWith(t => { });
                }
                catch
                {
                    // best effort
                }
            }

            private string RelativeBlobPath(string functionNameOrHost)
            {
                return string.Format("{0}/{1}.json", TestSiteName.ToLowerInvariant(), functionNameOrHost.ToLowerInvariant());
            }

            private string GetKeyVaultBaseUrl()
            {
                return $"https://{KeyVaultName}.vault.azure.net";
            }

            private string SecretsFileOrSentinelPath(string functionNameOrHost)
            {
                string secretFilePath = null;
                string fileName = string.Format(CultureInfo.InvariantCulture, "{0}.json", functionNameOrHost);
                secretFilePath = Path.Combine(SecretsDirectory, fileName);
                return secretFilePath;
            }

            public async Task WriteSecret(string functionNameOrHost, ScriptSecrets scriptSecret)
            {
                switch (RepositoryType)
                {
                    case SecretsRepositoryType.FileSystem:
                        WriteSecretsToFile(functionNameOrHost, ScriptSecretSerializer.SerializeSecrets(scriptSecret));
                        break;
                    case SecretsRepositoryType.BlobStorage:
                    case SecretsRepositoryType.BlobStorageSas:
                        await WriteSecretsBlobAndUpdateSentinelFile(functionNameOrHost, ScriptSecretSerializer.SerializeSecrets(scriptSecret));
                        break;
                    case SecretsRepositoryType.KeyVault:
                        await WriteSecretsKeyVaultAndUpdateSectinelFile(functionNameOrHost, scriptSecret);
                        break;
                    default:
                        break;
                }
            }

            private void WriteSecretsToFile(string functionNameOrHost, string fileText)
            {
                File.WriteAllText(SecretsFileOrSentinelPath(functionNameOrHost), fileText);
            }

            private async Task WriteSecretsBlobAndUpdateSentinelFile(string functionNameOrHost, string fileText, bool createSentinelFile = true)
            {
                string blobPath = RelativeBlobPath(functionNameOrHost);
                CloudBlockBlob secretBlob = BlobContainer.GetBlockBlobReference(blobPath);

                using (StreamWriter writer = new StreamWriter(await secretBlob.OpenWriteAsync()))
                {
                    writer.Write(fileText);
                }

                if (createSentinelFile)
                {
                    File.WriteAllText(SecretsFileOrSentinelPath(functionNameOrHost), " ");
                }
            }

            private async Task WriteSecretsKeyVaultAndUpdateSectinelFile(string functionNameOrHost, ScriptSecrets secrets, bool createSentinelFile = true)
            {
                Dictionary<string, string> dictionary = KeyVaultSecretsRepository.GetDictionaryFromScriptSecrets(secrets, functionNameOrHost);
                foreach (string key in dictionary.Keys)
                {
                    await KeyVaultClient.SetSecretAsync(GetKeyVaultBaseUrl(), key, dictionary[key]);
                }
            }

            public async Task<ScriptSecrets> GetSecretText(string functionNameOrHost, ScriptSecretsType type)
            {
                ScriptSecrets secrets = null;
                switch (RepositoryType)
                {
                    case SecretsRepositoryType.FileSystem:
                        string secretText = File.ReadAllText(SecretsFileOrSentinelPath(functionNameOrHost));
                        secrets = ScriptSecretSerializer.DeserializeSecrets(type, secretText);
                        break;
                    case SecretsRepositoryType.BlobStorage:
                    case SecretsRepositoryType.BlobStorageSas:
                        secrets = await GetSecretBlobText(functionNameOrHost, type);
                        break;
                    case SecretsRepositoryType.KeyVault:
                        secrets = await GetSecretsFromKeyVault(functionNameOrHost, type);
                        break;
                    default:
                        break;
                }
                return secrets;
            }

            private async Task<ScriptSecrets> GetSecretBlobText(string functionNameOrHost, ScriptSecretsType type)
            {
                string blobText = null;
                string blobPath = RelativeBlobPath(functionNameOrHost);
                if (await BlobContainer.GetBlockBlobReference(blobPath).ExistsAsync())
                {
                    blobText = await BlobContainer.GetBlockBlobReference(blobPath).DownloadTextAsync();
                }
                return ScriptSecretSerializer.DeserializeSecrets(type, blobText);
            }

            private async Task<ScriptSecrets> GetSecretsFromKeyVault(string functionNameOrHost, ScriptSecretsType type)
            {
                var secretResults = await KeyVaultClient.GetSecretsAsync(GetKeyVaultBaseUrl());
                if (type == ScriptSecretsType.Host)
                {
                    SecretBundle masterBundle = await KeyVaultClient.GetSecretAsync(GetKeyVaultBaseUrl(), secretResults.FirstOrDefault(x => x.Identifier.Name.StartsWith("host--master")).Identifier.Name);
                    SecretBundle functionKeyBundle = await KeyVaultClient.GetSecretAsync(GetKeyVaultBaseUrl(), secretResults.FirstOrDefault(x => x.Identifier.Name.StartsWith("host--functionKey")).Identifier.Name);
                    SecretBundle systemKeyBundle = await KeyVaultClient.GetSecretAsync(GetKeyVaultBaseUrl(), secretResults.FirstOrDefault(x => x.Identifier.Name.StartsWith("host--systemKey")).Identifier.Name);
                    HostSecrets hostSecrets = new HostSecrets()
                    {
                        FunctionKeys = new List<Key>() { new Key(GetSecretName(functionKeyBundle.SecretIdentifier.Name), functionKeyBundle.Value) },
                        SystemKeys = new List<Key>() { new Key(GetSecretName(systemKeyBundle.SecretIdentifier.Name), systemKeyBundle.Value) }
                    };
                    hostSecrets.MasterKey = new Key("master", masterBundle.Value);
                    return hostSecrets;
                }
                else
                {
                    SecretBundle functionKeyBundle = await KeyVaultClient.GetSecretAsync(GetKeyVaultBaseUrl(), secretResults.FirstOrDefault(x => x.Identifier.Name.StartsWith("function--")).Identifier.Name);
                    FunctionSecrets functionSecrets = new FunctionSecrets()
                    {
                        Keys = new List<Key>() { new Key(GetSecretName(functionKeyBundle.SecretIdentifier.Name), functionKeyBundle.Value) }
                    };
                    return functionSecrets;
                }
            }

            public bool MarkerFileExists(string functionNameOrHost)
            {
                return File.Exists(SecretsFileOrSentinelPath(functionNameOrHost));
            }

            private void ClearAllFileSecrets()
            {
                if (Directory.Exists(SecretsDirectory))
                {
                    var files = Directory.EnumerateFiles(SecretsDirectory);
                    foreach (string fileName in files)
                    {
                        File.Delete(Path.Combine(SecretsDirectory, fileName));
                    }
                }
            }

            private string GetSecretName(string secretName)
            {
                string[] array = secretName.Split("--");
                return KeyVaultSecretsRepository.Denormalize(array[array.Length - 1]);
            }

            private async Task ClearAllBlobSecrets()
            {
                // A sas connection requires the container to already exist, it
                // doesn't have permission to create it
                if (RepositoryType != SecretsRepositoryType.BlobStorageSas)
                {
                    await BlobContainer.CreateIfNotExistsAsync();
                }

                var blobs = await BlobContainer.ListBlobsSegmentedAsync(prefix: TestSiteName.ToLowerInvariant(), useFlatBlobListing: true,
                    blobListingDetails: BlobListingDetails.None, maxResults: 100, currentToken: null, options: null, operationContext: null);
                foreach (IListBlobItem blob in blobs.Results)
                {
                    await BlobContainer.GetBlockBlobReference(((CloudBlockBlob)blob).Name).DeleteIfExistsAsync();
                }
            }

            private async Task ClearAllKeyVaultSecrets()
            {
                foreach (SecretItem item in await KeyVaultClient.GetSecretsAsync(GetKeyVaultBaseUrl()))
                {
                    await KeyVaultClient.DeleteSecretAsync(GetKeyVaultBaseUrl(), item.Identifier.Name);
                }
            }
        }
    }
}