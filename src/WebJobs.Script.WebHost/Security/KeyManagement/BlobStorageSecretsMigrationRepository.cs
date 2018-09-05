// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class BlobStorageSecretsMigrationRepository : ISecretsRepository, IDisposable
    {
        private readonly string _secretSentinelDirectoryPath;
        private readonly string _accountConnectionString;
        private readonly string _siteSlotName;
        private readonly string _migrateSentinelPath;
        private readonly ILogger _logger;
        private readonly Lazy<Task<BlobStorageSecretsRepository>> _blobStorageSecretsRepositoryTask;

        public BlobStorageSecretsMigrationRepository(string secretSentinelDirectoryPath, string accountConnectionString, string siteSlotName, ILogger logger)
        {
            _secretSentinelDirectoryPath = secretSentinelDirectoryPath;
            _migrateSentinelPath = Path.Combine(_secretSentinelDirectoryPath, "migrate-sentinel.json");
            _accountConnectionString = accountConnectionString;
            _siteSlotName = siteSlotName;
            _logger = logger;

            _blobStorageSecretsRepositoryTask = new Lazy<Task<BlobStorageSecretsRepository>>(BlobStorageSecretsRepositoryFactory);
        }

        public event EventHandler<SecretsChangedEventArgs> SecretsChanged;

        private BlobStorageSecretsRepository BlobStorageSecretsRepository => _blobStorageSecretsRepositoryTask.Value.Result;

        private void BlobStorageSecretsMigrationRepository_SecretsChanged(object sender, SecretsChangedEventArgs e)
        {
            SecretsChanged?.Invoke(this, e);
        }

        private async Task<BlobStorageSecretsRepository> BlobStorageSecretsRepositoryFactory()
        {
            BlobStorageSecretsRepository blobStorageSecretsRepository = null;
            try
            {
                blobStorageSecretsRepository = new BlobStorageSecretsRepository(_secretSentinelDirectoryPath, _accountConnectionString, _siteSlotName);
                blobStorageSecretsRepository.SecretsChanged += BlobStorageSecretsMigrationRepository_SecretsChanged;
                await CopyKeysFromFileSystemToBlobStorage(blobStorageSecretsRepository);
                return blobStorageSecretsRepository;
            }
            catch (Exception ex)
            {
                _logger?.LogTrace("Secret keys migration is failed. {0}", ex.ToString());

                // Delete sentinel file and secrets container so we can try to copy keys once again
                File.Delete(_migrateSentinelPath);
                await blobStorageSecretsRepository?.BlobContainer.DeleteIfExistsAsync();

                throw new InvalidOperationException($"Secret keys migration is failed.", ex);
            }
        }

        public async Task<string> ReadAsync(ScriptSecretsType type, string functionName)
        {
            return await BlobStorageSecretsRepository.ReadAsync(type, functionName);
        }

        public async Task WriteAsync(ScriptSecretsType type, string functionName, string secretsContent)
        {
            await BlobStorageSecretsRepository.WriteAsync(type, functionName, secretsContent);
        }

        public async Task WriteSnapshotAsync(ScriptSecretsType type, string functionName, string secretsContent)
        {
            await BlobStorageSecretsRepository.WriteSnapshotAsync(type, functionName, secretsContent);
        }

        public async Task PurgeOldSecretsAsync(IList<string> currentFunctions, ILogger logger)
        {
            await BlobStorageSecretsRepository.PurgeOldSecretsAsync(currentFunctions, logger);
        }

        public async Task<string[]> GetSecretSnapshots(ScriptSecretsType type, string functionName)
        {
            return await BlobStorageSecretsRepository.GetSecretSnapshots(type, functionName);
        }

        public void Dispose()
        {
            BlobStorageSecretsRepository.Dispose();
        }

        private async Task CopyKeysFromFileSystemToBlobStorage(BlobStorageSecretsRepository blobStorageSecretsRepository)
        {
            if (File.Exists(_migrateSentinelPath))
            {
                // Migration is already done
                _logger?.LogTrace("Sentinel file is detected.");
                return;
            }

            // Create sentinel before migration as we do not want other instances to perform migration
            try
            {
                using (var stream = new FileStream(_migrateSentinelPath, FileMode.CreateNew))
                using (var writer = new StreamWriter(stream))
                {
                    //write file
                    _logger?.LogTrace("Sentinel file created.");
                }
            }
            catch (IOException)
            {
                _logger?.LogTrace("Sentinel file is already created by another instance.");
                return;
            }

            string[] files = Directory.GetFiles(blobStorageSecretsRepository.SecretsSentinelFilePath.Replace("Sentinels", string.Empty));
            if (await blobStorageSecretsRepository.BlobContainer.ExistsAsync())
            {
                BlobResultSegment resultSegment = await blobStorageSecretsRepository.BlobContainer.ListBlobsSegmentedAsync(blobStorageSecretsRepository.SecretsBlobPath + "/", null);

                // Check for conflicts
                if (resultSegment.Results.ToArray().Length > 0)
                {
                    _logger?.LogTrace("Conflict detected. Secrets container is not empty.");
                    return;
                }
            }
            else
            {
                await blobStorageSecretsRepository.BlobContainer.CreateIfNotExistsAsync();
            }

            if (files.Length > 0)
            {
                List<Task> copyTasks = new List<Task>();
                foreach (string file in files)
                {
                    string blobName = Path.GetFileName(file);
                    CloudBlockBlob cloudBlockBlob = blobStorageSecretsRepository.BlobContainer.GetBlockBlobReference(blobStorageSecretsRepository.SecretsBlobPath + "/" + blobName);

                    string contents = File.ReadAllText(file);
                    Task copyTask = cloudBlockBlob.UploadTextAsync(contents);
                    copyTasks.Add(copyTask);
                    _logger?.LogTrace("'{0}' was migrated.", cloudBlockBlob.StorageUri.PrimaryUri.AbsoluteUri.ToString());
                }
                await Task.WhenAll(copyTasks);
            }
            _logger?.LogTrace("Finished successfully.");
        }
    }
}
