// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebJobs.Script.Tests;
using Xunit;
using static Microsoft.Azure.WebJobs.Script.Tests.SecretsRepositoryTests;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Host
{
    public class SecretsRepositoryMigrationTests : IClassFixture<SecretsRepositoryMigrationTests.Fixture>
    {
        private readonly SecretsRepositoryMigrationTests.Fixture _fixture;
        private readonly ScriptSettingsManager _settingsManager;

        public SecretsRepositoryMigrationTests(SecretsRepositoryMigrationTests.Fixture fixture)
        {
            _fixture = fixture;
            _settingsManager = ScriptSettingsManager.Instance;
        }

        [Fact]
        public async Task SecretMigrate_Successful()
        {
            using (var directory = new TempDirectory())
            {
                try
                {

                    await _fixture.TestInitialize(SecretsRepositoryType.FileSystem, directory.Path);
                    var loggerProvider = new TestLoggerProvider();

                    var fileRepo = _fixture.GetFileSystemSecretsRepository();
                    string hostContent = Guid.NewGuid().ToString();
                    string functionContent = Guid.NewGuid().ToString();
                    await fileRepo.WriteAsync(ScriptSecretsType.Host, "host", hostContent);
                    await fileRepo.WriteAsync(ScriptSecretsType.Function, "test1", functionContent);

                    _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteSlotName, "Production");
                    _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteName, "test-app");

                    var blobRepoMigration = _fixture.GetBlobStorageSecretsMigrationRepository(loggerProvider.CreateLogger(ScriptConstants.LogCategoryMigration));

                    await blobRepoMigration.ReadAsync(ScriptSecretsType.Host, "host");
                    var logs = loggerProvider.GetAllLogMessages().ToArray();
                    Assert.Contains(logs[logs.Length - 1].FormattedMessage, "Finished successfully.");
                    string hostContentFromBlob = await blobRepoMigration.ReadAsync(ScriptSecretsType.Host, "");
                    Assert.Equal(hostContent, hostContentFromBlob);
                    string hostContentFromFunction = await blobRepoMigration.ReadAsync(ScriptSecretsType.Function, "test1");
                    Assert.Equal(functionContent, hostContentFromFunction);

                    var blobRepoMigration2 = _fixture.GetBlobStorageSecretsMigrationRepository(loggerProvider.CreateLogger(""));
                    await blobRepoMigration2.ReadAsync(ScriptSecretsType.Host, "host");
                    logs = loggerProvider.GetAllLogMessages().ToArray();
                    Assert.Contains(logs[logs.Length - 1].FormattedMessage, "Sentinel file is detected.");
                }
                finally
                {
                    _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteSlotName, null);
                    _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteName, null);
                }
            }
        }

        [Fact]
        public async Task SecretMigrate_Conflict()
        {
            using (var directory = new TempDirectory())
            {
                try
                {
                    await _fixture.TestInitialize(SecretsRepositoryType.FileSystem, directory.Path);
                    var loggerProvider = new TestLoggerProvider();

                    var fileRepo = _fixture.GetFileSystemSecretsRepository();
                    string hostContent = Guid.NewGuid().ToString();
                    string functionContent = Guid.NewGuid().ToString();
                    await fileRepo.WriteAsync(ScriptSecretsType.Host, "host", hostContent);
                    await fileRepo.WriteAsync(ScriptSecretsType.Function, "test1", functionContent);

                    _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteSlotName, "Production");
                    _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteName, "test-app");

                    var blobRepo = _fixture.GetBlobStorageRepository();
                    await blobRepo.WriteAsync(ScriptSecretsType.Host, "host", hostContent);
                    await blobRepo.WriteAsync(ScriptSecretsType.Function, "test1", functionContent);


                    var blobRepoMigration = _fixture.GetBlobStorageSecretsMigrationRepository(loggerProvider.CreateLogger(ScriptConstants.LogCategoryMigration));
                    await blobRepoMigration.ReadAsync(ScriptSecretsType.Host, "host");

                    var logs = loggerProvider.GetAllLogMessages().ToArray();
                    Assert.Contains("Conflict detected", loggerProvider.GetAllLogMessages().ToList().Last().FormattedMessage);
                }
                finally
                {
                    _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteSlotName, null);
                    _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteName, null);
                }
            }
        }

        [Fact]
        public async Task SecretMigrate_Failed()
        {
            using (var directory = new TempDirectory())
            {
                try
                {
                    await _fixture.TestInitialize(SecretsRepositoryType.FileSystem, directory.Path);
                    var loggerProvider = new TestLoggerProvider();

                    var fileRepo = _fixture.GetFileSystemSecretsRepository();
                    string hostContent = Guid.NewGuid().ToString();
                    string functionContent = Guid.NewGuid().ToString();
                    await fileRepo.WriteAsync(ScriptSecretsType.Host, "host", hostContent);
                    await fileRepo.WriteAsync(ScriptSecretsType.Function, "test1", functionContent);

                    _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteSlotName, "Production");
                    _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteName, "test-app");

                    var repo = _fixture.GetBrowkenBlobStorageSecretsMigrationRepository(loggerProvider.CreateLogger(ScriptConstants.LogCategoryMigration));
                    Assert.Throws(typeof(AggregateException), () =>
                    {
                        repo.ReadAsync(ScriptSecretsType.Host, string.Empty).GetAwaiter().GetResult();
                    });
                    var logs = loggerProvider.GetAllLogMessages().ToArray();
                    Assert.Contains("Secret keys migration is failed", loggerProvider.GetAllLogMessages().ToList().Last().FormattedMessage);

                    var repo1 = _fixture.GetBlobStorageSecretsMigrationRepository(loggerProvider.CreateLogger(ScriptConstants.LogCategoryMigration));
                    string hostContentFromBlob1 = await repo1.ReadAsync(ScriptSecretsType.Host, string.Empty);
                    var logs1 = loggerProvider.GetAllLogMessages().ToArray();

                    Assert.Equal(hostContent, hostContentFromBlob1);
                }
                finally
                {
                    _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteSlotName, null);
                    _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteName, null);
                }
            }
        }

        public class Fixture : SecretsRepositoryTests.Fixture
        {
            public Fixture() : base()
            {
            }

            public ISecretsRepository GetFileSystemSecretsRepository()
            {
                return new FileSystemSecretsRepository(SecretsDirectory);
            }

            public ISecretsRepository GetBlobStorageRepository()
            {
                return new BlobStorageSecretsRepository(Path.Combine(SecretsDirectory, "Sentinels"), BlobConnectionString, TestSiteName);
            }

            public BlobStorageSecretsMigrationRepository GetBlobStorageSecretsMigrationRepository(ILogger logger)
            {
                var repo = new BlobStorageSecretsMigrationRepository(Path.Combine(SecretsDirectory, "Sentinels"), BlobConnectionString, TestSiteName, logger);
                return repo;
            }

            public BlobStorageSecretsMigrationRepository GetBrowkenBlobStorageSecretsMigrationRepository(ILogger logger)
            {
                var repo = new BlobStorageSecretsMigrationRepository(Path.Combine(SecretsDirectory, "Sentinels"), string.Empty, TestSiteName, logger);
                return repo;
            }
        }
    }
}
