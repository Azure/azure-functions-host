// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.WebHost;
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

        public SecretsRepositoryTests(SecretsRepositoryTests.Fixture fixture)
        {
            _fixture = fixture;
        }

        public enum SecretsRepositoryType
        {
            FileSystem,
            BlobStorage
        }

        [Fact]
        public async Task FileSystemRepo_Constructor_CreatesSecretPathIfNotExists()
        {
            await Constructor_CreatesSecretPathIfNotExists(SecretsRepositoryType.FileSystem);
        }

        [Fact]
        public async Task BlobStorageRepo_Constructor_CreatesSecretPathIfNotExists()
        {
            await Constructor_CreatesSecretPathIfNotExists(SecretsRepositoryType.BlobStorage);
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
        [InlineData(ScriptSecretsType.Host)]
        [InlineData(ScriptSecretsType.Function)]
        public async Task BlobStorageRepo_ReadAsync_ReadsExpectedFile(ScriptSecretsType secretsType)
        {
            await ReadAsync_ReadsExpectedFile(SecretsRepositoryType.BlobStorage, secretsType);
        }

        [Theory]
        [InlineData(ScriptSecretsType.Host)]
        [InlineData(ScriptSecretsType.Function)]
        public async Task FileSystemRepo_ReadAsync_ReadsExpectedFile(ScriptSecretsType secretsType)
        {
            await ReadAsync_ReadsExpectedFile(SecretsRepositoryType.FileSystem, secretsType);
        }

        private async Task ReadAsync_ReadsExpectedFile(SecretsRepositoryType repositoryType, ScriptSecretsType secretsType)
        {
            using (var directory = new TempDirectory())
            {
                await _fixture.TestInitialize(repositoryType, directory.Path);
                string testContent = "test";
                string testFunctionName = secretsType == ScriptSecretsType.Host ? "host" : "testfunction";

                await _fixture.WriteSecret(testFunctionName, testContent);

                var target = _fixture.GetNewSecretRepository();

                string secretsContent = await target.ReadAsync(secretsType, testFunctionName);

                Assert.Equal(testContent, secretsContent);
            }
        }

        [Theory]
        [InlineData(ScriptSecretsType.Host)]
        [InlineData(ScriptSecretsType.Function)]
        public async Task BlobStorageRepo_WriteAsync_CreatesExpectedFile(ScriptSecretsType secretsType)
        {
            await WriteAsync_CreatesExpectedFile(SecretsRepositoryType.BlobStorage, secretsType);
        }

        [Theory]
        [InlineData(ScriptSecretsType.Host)]
        [InlineData(ScriptSecretsType.Function)]
        public async Task FileSystemRepo_WriteAsync_CreatesExpectedFile(ScriptSecretsType secretsType)
        {
            await WriteAsync_CreatesExpectedFile(SecretsRepositoryType.FileSystem, secretsType);
        }

        private async Task WriteAsync_CreatesExpectedFile(SecretsRepositoryType repositoryType, ScriptSecretsType secretsType)
        {
            using (var directory = new TempDirectory())
            {
                await _fixture.TestInitialize(repositoryType, directory.Path);
                string testContent = "test";
                string testFunctionName = secretsType == ScriptSecretsType.Host ? null : "TestFunction";

                var target = _fixture.GetNewSecretRepository();
                await target.WriteAsync(secretsType, testFunctionName, testContent);

                string filePath = Path.Combine(directory.Path, $"{testFunctionName ?? "host"}.json");

                if (repositoryType == SecretsRepositoryType.BlobStorage)
                {
                    Assert.True(_fixture.MarkerFileExists(testFunctionName ?? "host"));
                }
                Assert.Equal(testContent, await _fixture.GetSecretText(testFunctionName ?? "host"));
            }
        }

        [Fact]
        public async Task FileSystemRepo_WriteAsync_ChangeNotificationUpdatesExistingSecret()
        {
            await WriteAsync_ChangeNotificationUpdatesExistingSecret(SecretsRepositoryType.FileSystem);
        }

        [Fact]
        public async Task BlobStorageRepo_WriteAsync_ChangeNotificationUpdatesExistingSecret()
        {
            await WriteAsync_ChangeNotificationUpdatesExistingSecret(SecretsRepositoryType.BlobStorage);
        }

        private async Task WriteAsync_ChangeNotificationUpdatesExistingSecret(SecretsRepositoryType repositoryType)
        {
            using (var directory = new TempDirectory())
            {
                await _fixture.TestInitialize(repositoryType, directory.Path);

                string testFunctionName = "TestFunction";
                ScriptSecretsType secretsType = ScriptSecretsType.Function;
                string initialSecretText = "TextOne";
                string updatedSecretText = "TextTwo";

                await _fixture.WriteSecret(testFunctionName, initialSecretText);
                var target = _fixture.GetNewSecretRepository();
                string preTextResult = await target.ReadAsync(secretsType, testFunctionName);
                await _fixture.WriteSecret(testFunctionName, updatedSecretText);
                string postTextResult = await target.ReadAsync(secretsType, testFunctionName);

                Assert.Equal(initialSecretText, preTextResult);
                Assert.Equal(updatedSecretText, postTextResult);
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
        public async Task GetSecretSnapshots_ReturnsExpected(SecretsRepositoryType repositoryType, ScriptSecretsType secretsType)
        {
            using (var directory = new TempDirectory())
            {
                _fixture.TestInitialize(repositoryType, directory.Path);

                string testContent = "test";
                string testFunctionName = secretsType == ScriptSecretsType.Host ? null : "TestFunction";

                var target = _fixture.GetNewSecretRepository();
                await target.WriteAsync(secretsType, testFunctionName, testContent);
                for (int i = 0; i < 5; i++)
                {
                    await target.WriteSnapshotAsync(secretsType, testFunctionName, testContent);
                }
                string[] files = await target.GetSecretSnapshots(secretsType, testFunctionName);

                Assert.True(files.Length > 0);
            }
        }

        public class Fixture : IDisposable
        {
            public Fixture()
            {
                TestSiteName = "TestSiteName";
                BlobConnectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Storage);
                BlobContainer = CloudStorageAccount.Parse(BlobConnectionString).CreateCloudBlobClient().GetContainerReference("azure-webjobs-secrets");
            }

            public string TestSiteName { get; private set; }

            public string SecretsDirectory { get; private set; }

            public string BlobConnectionString { get; private set; }

            public CloudBlobContainer BlobContainer { get; private set; }

            public SecretsRepositoryType RepositoryType { get; private set; }

            public async Task TestInitialize(SecretsRepositoryType repositoryType, string secretsDirectory, string testSiteName = null)
            {
                RepositoryType = repositoryType;
                SecretsDirectory = secretsDirectory;
                if (testSiteName != null)
                {
                    TestSiteName = testSiteName;
                }

                await ClearAllBlobSecrets();
                ClearAllFileSecrets();
            }

            public ISecretsRepository GetNewSecretRepository()
            {
                if (RepositoryType == SecretsRepositoryType.BlobStorage)
                {
                    return new BlobStorageSecretsRepository(SecretsDirectory, BlobConnectionString, TestSiteName);
                }
                return new FileSystemSecretsRepository(SecretsDirectory);
            }

            public void Dispose()
            {
                try
                {
                    // delete blob files
                    ClearAllBlobSecrets().ContinueWith(t => { });
                    ClearAllFileSecrets();
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

            private string SecretsFileOrSentinelPath(string functionNameOrHost)
            {
                string secretFilePath = null;
                string fileName = string.Format(CultureInfo.InvariantCulture, "{0}.json", functionNameOrHost);
                secretFilePath = Path.Combine(SecretsDirectory, fileName);
                return secretFilePath;
            }

            public async Task WriteSecret(string functionNameOrHost, string fileText)
            {
                switch (RepositoryType)
                {
                    case SecretsRepositoryType.FileSystem:
                        WriteSecretsToFile(functionNameOrHost, fileText);
                        break;
                    case SecretsRepositoryType.BlobStorage:
                        await WriteSecretsBlobAndUpdateSentinelFile(functionNameOrHost, fileText);
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

            public async Task<string> GetSecretText(string functionNameOrHost)
            {
                string secretText = null;
                switch (RepositoryType)
                {
                    case SecretsRepositoryType.FileSystem:
                        secretText = File.ReadAllText(SecretsFileOrSentinelPath(functionNameOrHost));
                        break;
                    case SecretsRepositoryType.BlobStorage:
                        secretText = await GetSecretBlobText(functionNameOrHost);
                        break;
                    default:
                        break;
                }
                return secretText;
            }

            private async Task<string> GetSecretBlobText(string functionNameOrHost)
            {
                string blobText = null;
                string blobPath = RelativeBlobPath(functionNameOrHost);
                if (await BlobContainer.GetBlockBlobReference(blobPath).ExistsAsync())
                {
                    blobText = await BlobContainer.GetBlockBlobReference(blobPath).DownloadTextAsync();
                }
                return blobText;
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

            private async Task ClearAllBlobSecrets()
            {
                await BlobContainer.CreateIfNotExistsAsync();
                var blobs = await BlobContainer.ListBlobsSegmentedAsync(prefix: TestSiteName.ToLowerInvariant(), useFlatBlobListing: true,
                    blobListingDetails: BlobListingDetails.None, maxResults: 100, currentToken: null, options: null, operationContext: null);
                foreach (IListBlobItem blob in blobs.Results)
                {
                    await BlobContainer.GetBlockBlobReference(((CloudBlockBlob)blob).Name).DeleteIfExistsAsync();
                }
            }
        }
    }
}