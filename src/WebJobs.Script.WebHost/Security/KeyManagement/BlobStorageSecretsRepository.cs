// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics.Extensions;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    /// <summary>
    /// An <see cref="ISecretsRepository"/> implementation that uses Azure blob storage as the backing store.
    /// </summary>
    public class BlobStorageSecretsRepository : BaseSecretsRepository
    {
        private readonly string _secretsBlobPath;
        private readonly string _hostSecretsBlobPath;
        private readonly string _secretsContainerName = "azure-webjobs-secrets";
        private readonly string _accountConnection;
        private readonly IAzureBlobStorageProvider _azureBlobStorageProvider;
        private BlobContainerClient _blobContainerClient;

        public BlobStorageSecretsRepository(string secretSentinelDirectoryPath, string accountConnection, string siteSlotName, ILogger logger, IEnvironment environment, IAzureBlobStorageProvider azureBlobStorageProvider)
            : base(secretSentinelDirectoryPath, logger, environment)
        {
            if (secretSentinelDirectoryPath == null)
            {
                throw new ArgumentNullException(nameof(secretSentinelDirectoryPath));
            }
            if (accountConnection == null)
            {
                throw new ArgumentNullException(nameof(accountConnection));
            }
            if (siteSlotName == null)
            {
                throw new ArgumentNullException(nameof(siteSlotName));
            }

            _secretsBlobPath = siteSlotName.ToLowerInvariant();
            _hostSecretsBlobPath = string.Format("{0}/{1}", _secretsBlobPath, ScriptConstants.HostMetadataFileName);
            _accountConnection = accountConnection;
            _azureBlobStorageProvider = azureBlobStorageProvider;
        }

        private BlobContainerClient Container
        {
            get
            {
                if (_blobContainerClient == null)
                {
                    _blobContainerClient = CreateBlobContainerClient(_accountConnection);
                }
                return _blobContainerClient;
            }
        }

        public override bool IsEncryptionSupported
        {
            get
            {
                return false;
            }
        }

        public override string Name => nameof(BlobStorageSecretsRepository);

        protected virtual BlobContainerClient CreateBlobContainerClient(string connection)
        {
            if (_azureBlobStorageProvider.TryCreateBlobServiceClientFromConnection(connection, out BlobServiceClient blobServiceClient))
            {
                var blobContainerClient = blobServiceClient.GetBlobContainerClient(_secretsContainerName);
                blobContainerClient.CreateIfNotExists();

                return blobContainerClient;
            }

            throw new InvalidOperationException("Could not create BlobContainerClient");
        }

        public override async Task<ScriptSecrets> ReadAsync(ScriptSecretsType type, string functionName)
        {
            string secretsContent = null;
            string blobPath = GetSecretsBlobPath(type, functionName);
            try
            {
                BlobClient secretBlobClient = Container.GetBlobClient(blobPath);
                if (await secretBlobClient.ExistsAsync())
                {
                    var downloadResponse = await secretBlobClient.DownloadAsync();
                    using (StreamReader reader = new StreamReader(downloadResponse.Value.Content))
                    {
                        secretsContent = reader.ReadToEnd();
                    }
                }
            }
            catch (Exception)
            {
                LogErrorMessage("read");
                throw;
            }

            return string.IsNullOrEmpty(secretsContent) ? null : ScriptSecretSerializer.DeserializeSecrets(type, secretsContent);
        }

        public override async Task WriteAsync(ScriptSecretsType type, string functionName, ScriptSecrets secrets)
        {
            if (secrets == null)
            {
                throw new ArgumentNullException(nameof(secrets));
            }

            string blobPath = GetSecretsBlobPath(type, functionName);
            try
            {
                await WriteToBlobAsync(blobPath, ScriptSecretSerializer.SerializeSecrets(secrets));
            }
            catch (Exception)
            {
                LogErrorMessage("write");
                throw;
            }

            string filePath = GetSecretsSentinelFilePath(type, functionName);
            await FileUtility.WriteAsync(filePath, DateTime.UtcNow.ToString());
        }

        public override async Task WriteSnapshotAsync(ScriptSecretsType type, string functionName, ScriptSecrets secrets)
        {
            if (secrets == null)
            {
                throw new ArgumentNullException(nameof(secrets));
            }

            string blobPath = GetSecretsBlobPath(type, functionName);
            blobPath = SecretsUtility.GetNonDecryptableName(blobPath);

            try
            {
                await WriteToBlobAsync(blobPath, ScriptSecretSerializer.SerializeSecrets(secrets));
            }
            catch (Exception)
            {
                LogErrorMessage("write");
                throw;
            }
        }

        public override async Task PurgeOldSecretsAsync(IList<string> currentFunctions, ILogger logger)
        {
            // no-op - allow stale secrets to remain
            await Task.Yield();
        }

        public override async Task<string[]> GetSecretSnapshots(ScriptSecretsType type, string functionName)
        {
            // Prefix is secret blob path without extension
            string prefix = Path.GetFileNameWithoutExtension(GetSecretsBlobPath(type, functionName)) + $".{ScriptConstants.Snapshot}";

            var blobList = new List<string>();
            try
            {
                var results = Container.GetBlobsAsync(prefix: string.Format("{0}/{1}", _secretsBlobPath, prefix.ToLowerInvariant()));
                await foreach (BlobItem item in results)
                {
                    blobList.Add(item.Name);
                }
            }
            catch (Exception)
            {
                LogErrorMessage("list");
                throw;
            }
            return blobList.ToArray();
        }

        private string GetSecretsBlobPath(ScriptSecretsType secretsType, string functionName = null)
        {
            return secretsType == ScriptSecretsType.Host
                ? _hostSecretsBlobPath
                : string.Format("{0}/{1}", _secretsBlobPath, GetSecretFileName(functionName));
        }

        private async Task WriteToBlobAsync(string blobPath, string secretsContent)
        {
            BlockBlobClient secretBlobClient = Container.GetBlockBlobClient(blobPath);
            using (StreamWriter writer = new StreamWriter(await secretBlobClient.OpenWriteAsync(true)))
            {
                await writer.WriteAsync(secretsContent);
            }
        }

        protected virtual void LogErrorMessage(string operation)
        {
            Logger?.BlobStorageSecretRepoError(operation, "AzureWebJobsStorage");
        }
    }
}