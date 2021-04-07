// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
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
        private readonly string _accountConnectionString;
        private CloudBlobContainer _blobContainer;

        public BlobStorageSecretsRepository(string secretSentinelDirectoryPath, string accountConnectionString, string siteSlotName, ILogger logger, IEnvironment environment)
            : base(secretSentinelDirectoryPath, logger, environment)
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
        }

        private CloudBlobContainer Container
        {
            get
            {
                if (_blobContainer == null)
                {
                    _blobContainer = CreateBlobContainer(_accountConnectionString);
                }
                return _blobContainer;
            }
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
            var account = CloudStorageAccount.Parse(connectionString);
            var client = account.CreateCloudBlobClient();
            var container = client.GetContainerReference(_secretsContainerName);

            container.CreateIfNotExists();

            return container;
        }

        public override async Task<ScriptSecrets> ReadAsync(ScriptSecretsType type, string functionName)
        {
            string secretsContent = null;
            string blobPath = GetSecretsBlobPath(type, functionName);
            try
            {
                CloudBlockBlob secretBlob = Container.GetBlockBlobReference(blobPath);
                if (await secretBlob.ExistsAsync())
                {
                    secretsContent = await secretBlob.DownloadTextAsync();
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

            BlobResultSegment segmentResult;
            try
            {
                segmentResult = await Container.ListBlobsSegmentedAsync(string.Format("{0}/{1}", _secretsBlobPath, prefix.ToLowerInvariant()), null);
            }
            catch (Exception)
            {
                LogErrorMessage("list");
                throw;
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
            CloudBlockBlob secretBlob = Container.GetBlockBlobReference(blobPath);
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