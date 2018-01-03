// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.IO;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    /// <summary>
    /// An <see cref="ISecretsRepository"/> implementation that uses Azure blob storage as the backing store.
    /// </summary>
    public sealed class BlobStorageSecretsRepository : ISecretsRepository, IDisposable
    {
        private readonly string _secretsSentinelFilePath;
        private readonly string _secretsBlobPath;
        private readonly string _hostSecretsSentinelFilePath;
        private readonly string _hostSecretsBlobPath;
        private readonly AutoRecoveringFileSystemWatcher _sentinelFileWatcher;
        private readonly CloudBlobContainer _blobContainer;
        private readonly string _secretsContainerName = "azure-webjobs-secrets";
        private readonly string _accountConnectionString;
        private bool _disposed = false;

        public BlobStorageSecretsRepository(string secretSentinelDirectoryPath, string accountConnectionString, string siteSlotName)
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

            _secretsSentinelFilePath = secretSentinelDirectoryPath;
            _hostSecretsSentinelFilePath = Path.Combine(_secretsSentinelFilePath, ScriptConstants.HostMetadataFileName);

            Directory.CreateDirectory(_secretsSentinelFilePath);

            _sentinelFileWatcher = new AutoRecoveringFileSystemWatcher(_secretsSentinelFilePath, "*.json");
            _sentinelFileWatcher.Changed += OnChanged;

            _secretsBlobPath = siteSlotName.ToLowerInvariant();
            _hostSecretsBlobPath = string.Format("{0}/{1}", _secretsBlobPath, ScriptConstants.HostMetadataFileName);

            _accountConnectionString = accountConnectionString;
            CloudStorageAccount account = CloudStorageAccount.Parse(_accountConnectionString);
            CloudBlobClient client = account.CreateCloudBlobClient();

            _blobContainer = client.GetContainerReference(_secretsContainerName);
            _blobContainer.CreateIfNotExists();
        }

        public event EventHandler<SecretsChangedEventArgs> SecretsChanged;

        private string GetSecretsBlobPath(ScriptSecretsType secretsType, string functionName = null)
        {
            return secretsType == ScriptSecretsType.Host
                ? _hostSecretsBlobPath
                : string.Format("{0}/{1}", _secretsBlobPath, GetSecretFileName(functionName));
        }

        private string GetSecretsSentinelFilePath(ScriptSecretsType secretsType, string functionName = null)
        {
            return secretsType == ScriptSecretsType.Host
                ? _hostSecretsSentinelFilePath
                : Path.Combine(_secretsSentinelFilePath, GetSecretFileName(functionName));
        }

        private static string GetSecretFileName(string functionName)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}.json", functionName.ToLowerInvariant());
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            var changeHandler = SecretsChanged;
            if (changeHandler != null)
            {
                var args = new SecretsChangedEventArgs { SecretsType = ScriptSecretsType.Host };

                if (string.Compare(Path.GetFileName(e.FullPath), ScriptConstants.HostMetadataFileName, StringComparison.OrdinalIgnoreCase) != 0)
                {
                    args.SecretsType = ScriptSecretsType.Function;
                    args.Name = Path.GetFileNameWithoutExtension(e.FullPath).ToLowerInvariant();
                }

                changeHandler(this, args);
            }
        }

        public async Task<string> ReadAsync(ScriptSecretsType type, string functionName)
        {
            string secretsContent = null;
            string blobPath = GetSecretsBlobPath(type, functionName);
            CloudBlockBlob secretBlob = _blobContainer.GetBlockBlobReference(blobPath);

            if (await secretBlob.ExistsAsync())
            {
                secretsContent = await secretBlob.DownloadTextAsync();
            }

            return secretsContent;
        }

        public async Task WriteAsync(ScriptSecretsType type, string functionName, string secretsContent)
        {
            if (secretsContent == null)
            {
                throw new ArgumentNullException(nameof(secretsContent));
            }

            string blobPath = GetSecretsBlobPath(type, functionName);
            await WriteToBlobAsync(blobPath, secretsContent);

            string filePath = GetSecretsSentinelFilePath(type, functionName);
            await FileUtility.WriteAsync(filePath, DateTime.UtcNow.ToString());
        }

        public async Task WriteSnapshotAsync(ScriptSecretsType type, string functionName, string secretsContent)
        {
            if (secretsContent == null)
            {
                throw new ArgumentNullException(nameof(secretsContent));
            }

            string blobPath = GetSecretsBlobPath(type, functionName);
            blobPath = SecretsUtility.GetNonDecryptableName(blobPath);
            await WriteToBlobAsync(blobPath, secretsContent);
        }

        public async Task PurgeOldSecretsAsync(IList<string> currentFunctions, TraceWriter traceWriter, ILogger logger)
        {
            // no-op - allow stale secrets to remain
            await Task.Yield();
        }

        public async Task<string[]> GetSecretSnapshots(ScriptSecretsType type, string functionName)
        {
            // Prefix is secret blob path without extension
            string prefix = Path.GetFileNameWithoutExtension(GetSecretsBlobPath(type, functionName)) + $".{ScriptConstants.Snapshot}";

            BlobResultSegment segmentResult = await _blobContainer.ListBlobsSegmentedAsync(string.Format("{0}/{1}", _secretsBlobPath, prefix.ToLowerInvariant()), null);
            return segmentResult.Results.Select(x => x.Uri.ToString()).ToArray();
        }

        private async Task WriteToBlobAsync(string blobPath, string secretsContent)
        {
            CloudBlockBlob secretBlob = _blobContainer.GetBlockBlobReference(blobPath);
            using (StreamWriter writer = new StreamWriter(await secretBlob.OpenWriteAsync()))
            {
                await writer.WriteAsync(secretsContent);
            }
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _sentinelFileWatcher.Dispose();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}