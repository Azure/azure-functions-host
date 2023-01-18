// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.WebJobs.Script.Tests;
using WebJobs.Script.Tests;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class SecretsRepositoryTests : IClassFixture<SecretsRepositoryTests.Fixture>
    {
        private readonly SecretsRepositoryTests.Fixture _fixture;
        private readonly string functionName = "Test_test";
        public static string KeyName = "Te!@#st!1-te_st";

        private ITestOutputHelper _output;

        public SecretsRepositoryTests(SecretsRepositoryTests.Fixture fixture, ITestOutputHelper outputHelper)
        {
            Utility.ColdStartDelayMS = 50;
            _fixture = fixture;
            _output = outputHelper;
        }

        public enum SecretsRepositoryType
        {
            FileSystem,
            BlobStorage,
            BlobStorageSas,
            KeyVault
        }

        [Theory]
        [InlineData(SecretsRepositoryType.BlobStorage, "Dedicated")]
        [InlineData(SecretsRepositoryType.BlobStorage, "Dynamic")]
        [InlineData(SecretsRepositoryType.BlobStorageSas, "Dedicated")]
        [InlineData(SecretsRepositoryType.FileSystem, "Dedicated")]
        [InlineData(SecretsRepositoryType.KeyVault, "Dedicated")]
        public async Task Constructor_CreatesSecretPathIfNotExists(SecretsRepositoryType repositoryType, string sku)
        {
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            _fixture.Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, sku);
            await _fixture.TestInitialize(repositoryType, path);

            try
            {
                bool preConstDirExists = Directory.Exists(path);
                var target = _fixture.GetNewSecretRepository();

                if (_fixture.Environment.IsDynamicSku())
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(2 * Utility.ColdStartDelayMS));
                }

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
        [InlineData(SecretsRepositoryType.FileSystem, ScriptSecretsType.Host)]
        [InlineData(SecretsRepositoryType.FileSystem, ScriptSecretsType.Function)]
        [InlineData(SecretsRepositoryType.KeyVault, ScriptSecretsType.Host)]
        [InlineData(SecretsRepositoryType.KeyVault, ScriptSecretsType.Function)]
        public async Task ReadAsync_ReadsExpectedFile(SecretsRepositoryType repositoryType, ScriptSecretsType secretsType)
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
                        FunctionKeys = new List<Key>() { new Key(KeyName, "test") },
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

        [Theory] // Only for Key Vault to test paging over large number of secrets
        [InlineData(SecretsRepositoryType.KeyVault, ScriptSecretsType.Host)]
        [InlineData(SecretsRepositoryType.KeyVault, ScriptSecretsType.Function)]
        public async Task ReadAsync_ReadsExpectedKeyVaultPages(SecretsRepositoryType repositoryType, ScriptSecretsType secretsType)
        {
            using (var directory = new TempDirectory())
            {
                await _fixture.TestInitialize(repositoryType, directory.Path);
                ScriptSecrets testSecrets = null;
                int keyCount = 35;

                List<Key> functionKeys = new List<Key>();
                for (int i = 0; i < keyCount; ++i)
                {
                    functionKeys.Add(new Key(KeyName + Guid.NewGuid().ToString(), "test" + i.ToString()));
                }

                if (secretsType == ScriptSecretsType.Host)
                {
                    testSecrets = new HostSecrets()
                    {
                        MasterKey = new Key("master", "test"),
                        FunctionKeys = functionKeys,
                        SystemKeys = new List<Key>() { new Key(KeyName, "test") }
                    };
                }
                else
                {
                    testSecrets = new FunctionSecrets()
                    {
                        Keys = functionKeys
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

                    Assert.Equal((secretsContent as HostSecrets).FunctionKeys.Count, functionKeys.Count);
                    foreach (Key originalKey in functionKeys)
                    {
                        var matchingKeys = (secretsContent as HostSecrets).FunctionKeys.Where(x => string.Equals(x.Name, originalKey.Name));
                        Assert.Equal(matchingKeys.Count(), 1);
                        Assert.Equal(matchingKeys.First().Value, originalKey.Value);
                    }

                    Assert.Equal((secretsContent as HostSecrets).SystemKeys[0].Name, KeyName);
                    Assert.Equal((secretsContent as HostSecrets).SystemKeys[0].Value, "test");
                }
                else
                {
                    Assert.Equal((secretsContent as FunctionSecrets).Keys.Count, functionKeys.Count);
                    foreach (Key originalKey in functionKeys)
                    {
                        var matchingKeys = (secretsContent as FunctionSecrets).Keys.Where(x => string.Equals(x.Name, originalKey.Name));
                        Assert.Equal(matchingKeys.Count(), 1);
                        Assert.Equal(matchingKeys.First().Value, originalKey.Value);
                    }
                }

            }
        }


        [Theory]
        [InlineData(SecretsRepositoryType.BlobStorage, ScriptSecretsType.Host)]
        [InlineData(SecretsRepositoryType.BlobStorage, ScriptSecretsType.Function)]
        [InlineData(SecretsRepositoryType.BlobStorageSas, ScriptSecretsType.Host)]
        [InlineData(SecretsRepositoryType.BlobStorageSas, ScriptSecretsType.Function)]
        [InlineData(SecretsRepositoryType.FileSystem, ScriptSecretsType.Host)]
        [InlineData(SecretsRepositoryType.FileSystem, ScriptSecretsType.Function)]
        [InlineData(SecretsRepositoryType.KeyVault, ScriptSecretsType.Host)]
        [InlineData(SecretsRepositoryType.KeyVault, ScriptSecretsType.Function)]
        public async Task WriteAsync_CreatesExpectedFile(SecretsRepositoryType repositoryType, ScriptSecretsType secretsType)
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

        [Theory]
        [InlineData(SecretsRepositoryType.BlobStorage)]
        [InlineData(SecretsRepositoryType.BlobStorageSas)]
        [InlineData(SecretsRepositoryType.FileSystem)]
        public async Task WriteAsync_ChangeNotificationUpdatesExistingSecret(SecretsRepositoryType repositoryType)
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

        [Theory]
        [InlineData(SecretsRepositoryType.BlobStorage)]
        [InlineData(SecretsRepositoryType.BlobStorageSas)]
        public async Task BlobRepository_WriteAsync_DoesNot_ClearBlobContents(SecretsRepositoryType repositoryType)
        {
            using (var directory = new TempDirectory())
            {
                await _fixture.TestInitialize(repositoryType, directory.Path);
                string testFunctionName = "host";
                var target = _fixture.GetNewSecretRepository();

                async Task RunTest()
                {
                    HostSecrets testSecrets = new HostSecrets()
                    {
                        MasterKey = new Key("master", "test"),
                        FunctionKeys = new List<Key>(),
                        SystemKeys = new List<Key>() { new Key(KeyName, "test") }
                    };

                    // make the payload larger to guarantee the race condition
                    for (int i = 0; i < 1000; i++)
                    {
                        testSecrets.FunctionKeys.Add(new Key(KeyName + i, "test"));
                    }
                    // Set up initial secrets.
                    await _fixture.WriteSecret(testFunctionName, testSecrets);

                    // Perform a write and read similtaneously. Previously, our usage of OpenWriteAsync
                    // would erase the content of the blob while writing, resulting in null secrets from the
                    // read.
                    Task writeTask = target.WriteAsync(ScriptSecretsType.Host, testFunctionName, testSecrets);
                    HostSecrets secretsContent = await target.ReadAsync(ScriptSecretsType.Host, testFunctionName) as HostSecrets;

                    await writeTask;

                    Assert.NotNull(secretsContent);
                }

                // this is a race so it may not fire every time; try several times
                await TestHelpers.RetryFailedTest(RunTest, 10, _output);
            }
        }

        [Theory]
        [InlineData(SecretsRepositoryType.BlobStorage)]
        [InlineData(SecretsRepositoryType.BlobStorageSas)]
        public async Task BlobRepository_SimultaneousWrites_Throws_PreconditionFailed(SecretsRepositoryType repositoryType)
        {
            using (var directory = new TempDirectory())
            {
                await _fixture.TestInitialize(repositoryType, directory.Path);
                var target = _fixture.GetNewSecretRepository();
                string testFunctionName = "host";

                async Task RunTest()
                {
                    HostSecrets testSecrets = new HostSecrets()
                    {
                        MasterKey = new Key("master", "test"),
                        FunctionKeys = new List<Key>(),
                        SystemKeys = new List<Key>() { new Key(KeyName, "test") }
                    };

                    // make the payload larger to guarantee the race condition
                    for (int i = 0; i < 1000; i++)
                    {
                        testSecrets.FunctionKeys.Add(new Key(KeyName + i, "test"));
                    }

                    // Set up initial secrets.
                    await _fixture.WriteSecret(testFunctionName, testSecrets);
                    HostSecrets secretsContent = await target.ReadAsync(ScriptSecretsType.Host, testFunctionName) as HostSecrets;
                    Assert.Equal("test", secretsContent.FunctionKeys.First().Value);

                    testSecrets.FunctionKeys.First().Value = "changed";

                    // Simultaneous writes will result in one of the writes being discarded due to
                    // non-matching ETag.
                    Task writeTask1 = target.WriteAsync(ScriptSecretsType.Host, testFunctionName, testSecrets);
                    Task writeTask2 = target.WriteAsync(ScriptSecretsType.Host, testFunctionName, testSecrets);

                    var ex = await Assert.ThrowsAsync<RequestFailedException>(() => Task.WhenAll(writeTask1, writeTask2));

                    // Ensure the write went through.
                    secretsContent = await target.ReadAsync(ScriptSecretsType.Host, testFunctionName) as HostSecrets;
                    Assert.Equal("changed", secretsContent.FunctionKeys.First().Value);

                    Assert.Equal("ConditionNotMet", ex.ErrorCode);
                    Assert.Equal(412, ex.Status);
                    Assert.True(writeTask1.IsCompletedSuccessfully || writeTask2.IsCompletedSuccessfully,
                        "One of the write operations should have completed successfully.");
                }

                // this is a race so it may not fire every time; try several times
                await TestHelpers.RetryFailedTest(RunTest, 10, _output);
            }
        }

        [Theory]
        [InlineData(SecretsRepositoryType.BlobStorage)]
        [InlineData(SecretsRepositoryType.BlobStorageSas)]
        public async Task BlobRepository_SimultaneousCreates_Throws_Conflict(SecretsRepositoryType repositoryType)
        {
            using (var directory = new TempDirectory())
            {
                await _fixture.TestInitialize(repositoryType, directory.Path);
                string testFunctionName = "host";
                var target = _fixture.GetNewSecretRepository();

                async Task RunTest()
                {
                    HostSecrets testSecrets = new HostSecrets()
                    {
                        MasterKey = new Key("master", "test"),
                        FunctionKeys = new List<Key>(),
                        SystemKeys = new List<Key>() { new Key(KeyName, "test") }
                    };

                    // make the payload larger to guarantee the race condition
                    for (int i = 0; i < 1000; i++)
                    {
                        testSecrets.FunctionKeys.Add(new Key(KeyName + i, "test"));
                    }

                    // Ensure nothing is there.                
                    HostSecrets secretsContent = await target.ReadAsync(ScriptSecretsType.Host, testFunctionName) as HostSecrets;
                    Assert.Null(secretsContent);

                    // Simultaneous creates will result in one of the writes being discarded due to
                    // non-matching ETag.
                    Task writeTask1 = target.WriteAsync(ScriptSecretsType.Host, testFunctionName, testSecrets);
                    Task writeTask2 = target.WriteAsync(ScriptSecretsType.Host, testFunctionName, testSecrets);

                    var ex = await Assert.ThrowsAsync<RequestFailedException>(() => Task.WhenAll(writeTask1, writeTask2));

                    // Ensure the write went through.
                    secretsContent = await target.ReadAsync(ScriptSecretsType.Host, testFunctionName) as HostSecrets;
                    Assert.Equal("test", secretsContent.FunctionKeys.First().Value);

                    Assert.Equal("BlobAlreadyExists", ex.ErrorCode);
                    Assert.Equal(409, ex.Status);
                    Assert.True(writeTask1.IsCompletedSuccessfully || writeTask2.IsCompletedSuccessfully,
                        "One of the write operations should have completed successfully.");
                }

                // this is a race so it may not fire every time; try several times
                await TestHelpers.RetryFailedTest(RunTest, 10, _output);
            }
        }

        public class Fixture : IDisposable
        {
            public Fixture()
            {
                TestSiteName = "Test_test";
                Environment = new TestEnvironment();

                var configuration = TestHelpers.GetTestConfiguration();
                BlobConnectionString = configuration.GetWebJobsConnectionString(ConnectionStringNames.Storage);
                KeyVaultUri = configuration.GetWebJobsConnectionString(EnvironmentSettingNames.AzureWebJobsSecretStorageKeyVaultUri);
                KeyVaultClientId = configuration.GetWebJobsConnectionString(EnvironmentSettingNames.AzureWebJobsSecretStorageKeyVaultClientId);
                KeyVaultClientSecret = configuration.GetWebJobsConnectionString(EnvironmentSettingNames.AzureWebJobsSecretStorageKeyVaultClientSecret);
                KeyVaultTenantId = configuration.GetWebJobsConnectionString(EnvironmentSettingNames.AzureWebJobsSecretStorageKeyVaultTenantId);

                if (KeyVaultTenantId is not null && KeyVaultClientId is not null &&
                    KeyVaultClientSecret is not null && KeyVaultUri is not null)
                {
                    // These will fail later if required; but sometimes when testing locally you don't care about KeyVault
                    var credential = new ClientSecretCredential(KeyVaultTenantId, KeyVaultClientId, KeyVaultClientSecret);
                    SecretClient = new SecretClient(new Uri(KeyVaultUri), credential);
                }

                AzureBlobStorageProvider = TestHelpers.GetAzureBlobStorageProvider(configuration);
            }

            public IEnvironment Environment { get; private set; }

            public string TestSiteName { get; private set; }

            public string SecretsDirectory { get; private set; }

            public string BlobConnectionString { get; private set; }

            public Uri BlobSasConnectionUri { get; private set; }

            public CloudBlobContainer BlobContainer { get; private set; }

            public SecretClient SecretClient { get; private set; }

            public string KeyVaultUri { get; private set; }

            public string KeyVaultClientId { get; private set; }

            public string KeyVaultClientSecret { get; private set; }

            public string KeyVaultTenantId { get; private set; }

            public SecretsRepositoryType RepositoryType { get; private set; }

            public ILoggerProvider LoggerProvider { get; private set; }

            public IAzureBlobStorageProvider AzureBlobStorageProvider { get; private set; }

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

                if (SecretClient is not null)
                {
                    await ClearAllKeyVaultSecrets();
                }

                LoggerProvider = new TestLoggerProvider();
                var loggerFactory = new LoggerFactory();
                loggerFactory.AddProvider(LoggerProvider);
            }

            public ISecretsRepository GetNewSecretRepository()
            {
                var logger = LoggerProvider.CreateLogger("Test");
                if (RepositoryType == SecretsRepositoryType.BlobStorage)
                {
                    return new BlobStorageSecretsRepository(SecretsDirectory, ConnectionStringNames.Storage, TestSiteName, logger, Environment, AzureBlobStorageProvider);
                }
                else if (RepositoryType == SecretsRepositoryType.BlobStorageSas)
                {
                    return new BlobStorageSasSecretsRepository(SecretsDirectory, BlobSasConnectionUri.ToString(), TestSiteName, logger, Environment, AzureBlobStorageProvider);
                }
                else if (RepositoryType == SecretsRepositoryType.FileSystem)
                {
                    return new FileSystemSecretsRepository(SecretsDirectory, logger, Environment);
                }
                else
                {
                    return new KeyVaultSecretsRepository(SecretsDirectory, KeyVaultUri, KeyVaultClientId, KeyVaultClientSecret, KeyVaultTenantId, logger, Environment);
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
                    await SecretClient.SetSecretAsync(key, dictionary[key]);
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
                var secretResults = SecretClient.GetPropertiesOfSecretsAsync().AsPages();
                var searchPages = new List<SecretProperties>();
                await foreach (Page<SecretProperties> page in secretResults)
                {
                    {
                        foreach (SecretProperties secret in page.Values)
                        {
                            searchPages.Add(secret);
                        }
                    }
                }

                if (type == ScriptSecretsType.Host)
                {
                    KeyVaultSecret masterBundle = await SecretClient.GetSecretAsync(searchPages.FirstOrDefault(x => x.Name.StartsWith("host--master")).Name);
                    KeyVaultSecret functionKeyBundle = await SecretClient.GetSecretAsync(searchPages.FirstOrDefault(x => x.Name.StartsWith("host--functionKey")).Name);
                    KeyVaultSecret systemKeyBundle = await SecretClient.GetSecretAsync(searchPages.FirstOrDefault(x => x.Name.StartsWith("host--systemKey")).Name);
                    HostSecrets hostSecrets = new HostSecrets()
                    {
                        FunctionKeys = new List<Key>() { new Key(GetSecretName(functionKeyBundle.Name), functionKeyBundle.Value) },
                        SystemKeys = new List<Key>() { new Key(GetSecretName(systemKeyBundle.Name), systemKeyBundle.Value) }
                    };
                    hostSecrets.MasterKey = new Key("master", masterBundle.Value);
                    return hostSecrets;
                }
                else
                {
                    KeyVaultSecret functionKeyBundle = await SecretClient.GetSecretAsync(searchPages.FirstOrDefault(x => x.Name.StartsWith("function--")).Name);
                    FunctionSecrets functionSecrets = new FunctionSecrets()
                    {
                        Keys = new List<Key>() { new Key(GetSecretName(functionKeyBundle.Name), functionKeyBundle.Value) }
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
                var secretsPages = KeyVaultSecretsRepository.GetKeyVaultSecretsPagesAsync(SecretClient).AsPages();
                await foreach (Page<SecretProperties> page in secretsPages)
                {
                    foreach (SecretProperties item in page.Values)
                    {
                        await SecretClient.StartDeleteSecretAsync(item.Name);
                    }
                }
            }
        }
    }
}