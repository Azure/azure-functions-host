// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.IO;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Properties;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    /// <summary>
    /// An <see cref="ISecretsRepository"/> implementation that uses Azure blob storage as the backing store.
    /// </summary>
    public class BlobStorageSecretsRepository : BaseSecretsRepository
    {
        private readonly string _secretsBlobPath;
        private readonly string _hostSecretsBlobPath;
        private readonly CloudBlobContainer _blobContainer;
        private readonly string _secretsContainerName = "azure-webjobs-secrets";
        private readonly string _accountConnectionString;

        public BlobStorageSecretsRepository(string secretSentinelDirectoryPath, string accountConnectionString, string siteSlotName)
            : this(secretSentinelDirectoryPath, accountConnectionString, siteSlotName, null)
        {
        }

        public BlobStorageSecretsRepository(string secretSentinelDirectoryPath, string accountConnectionString, string siteSlotName, ILogger logger)
            : base(secretSentinelDirectoryPath, logger)
        {
            if (secretSentinelDirectoryPath == null)
            {
                throw new ArgumentNullException(nameof(secretSentinelDirectoryPath));
            }
            if (accountConnectionString == null)
            {
                throw new ArgumentNullException(nameof(accountConnectionString));
            }
            if (siteSlotName == null)
            {
                throw new ArgumentNullException(nameof(siteSlotName));
            }

            _secretsBlobPath = siteSlotName.ToLowerInvariant();
            _hostSecretsBlobPath = string.Format("{0}/{1}", _secretsBlobPath, ScriptConstants.HostMetadataFileName);

            _accountConnectionString = accountConnectionString;
            _blobContainer = CreateBlobContainer(_accountConnectionString);
        }

        public override bool IsEncryptionSupported
        {
            get
            {
                return false;
            }
        }

        protected virtual CloudBlobContainer CreateBlobContainer(string connectionString)
        {
            CloudStorageAccount account = CloudStorageAccount.Parse(_accountConnectionString);
            CloudBlobClient client = account.CreateCloudBlobClient();
            CloudBlobContainer container = client.GetContainerReference(_secretsContainerName);

            // TODO: Remove this (it is already slated to be removed)
            container.CreateIfNotExistsAsync().GetAwaiter().GetResult();

            return container;
        }

        public override async Task<ScriptSecrets> ReadAsync(ScriptSecretsType type, string functionName)
        {
            string secretsContent = null;
            string blobPath = GetSecretsBlobPath(type, functionName);
            try
            {
                CloudBlockBlob secretBlob = _blobContainer.GetBlockBlobReference(blobPath);
                if (await secretBlob.ExistsAsync())
                {
                    secretsContent = await secretBlob.DownloadTextAsync();
                }
            }
            catch (Exception e)
            {
                LogErrorMessage("read");
                throw e;
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
            catch (Exception e)
            {
                LogErrorMessage("write");
                throw e;
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
            catch (Exception e)
            {
                LogErrorMessage("write");
                throw e;
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

            BlobResultSegment segmentResult;
            try
            {
                segmentResult = await _blobContainer.ListBlobsSegmentedAsync(string.Format("{0}/{1}", _secretsBlobPath, prefix.ToLowerInvariant()), null);
            }
            catch (Exception e)
            {
                LogErrorMessage("list");
                throw e;
            }
            return segmentResult.Results.Select(x => x.Uri.ToString()).ToArray();
        }

        private string GetSecretsBlobPath(ScriptSecretsType secretsType, string functionName = null)
        {
            return secretsType == ScriptSecretsType.Host
                ? _hostSecretsBlobPath
                : string.Format("{0}/{1}", _secretsBlobPath, GetSecretFileName(functionName));
        }

        private async Task WriteToBlobAsync(string blobPath, string secretsContent)
        {
            CloudBlockBlob secretBlob = _blobContainer.GetBlockBlobReference(blobPath);
            using (StreamWriter writer = new StreamWriter(await secretBlob.OpenWriteAsync()))
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