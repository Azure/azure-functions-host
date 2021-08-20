// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Microsoft.Azure.Storage;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Host
{
    /// <summary>
    /// TODO: TEMP - this temporary implementation should be removed once https://github.com/Azure/azure-webjobs-sdk/issues/2710 is addressed.
    /// </summary>
    internal class BlobStorageConcurrencyStatusRepository : IConcurrencyStatusRepository
    {
        private const string HostContainerName = "azure-webjobs-hosts";
        private readonly IHostIdProvider _hostIdProvider;
        private readonly ILogger _logger;
        private readonly IAzureStorageProvider _azureStorageProvider;
        private BlobContainerClient? _blobContainerClient;

        public BlobStorageConcurrencyStatusRepository(IHostIdProvider hostIdProvider, ILoggerFactory loggerFactory, IAzureStorageProvider azureStorageProvider)
        {
            _hostIdProvider = hostIdProvider;
            _logger = loggerFactory.CreateLogger(LogCategories.Concurrency);
            _azureStorageProvider = azureStorageProvider;
        }

        public async Task<HostConcurrencySnapshot?> ReadAsync(CancellationToken cancellationToken)
        {
            string blobPath = await GetBlobPathAsync(cancellationToken);

            try
            {
                BlobContainerClient? containerClient = await GetContainerClientAsync(cancellationToken);
                if (containerClient != null)
                {
                    BlobClient blobClient = containerClient.GetBlobClient(blobPath);
                    string content;
                    var downloadResponse = await blobClient.DownloadAsync();
                    using (StreamReader reader = new StreamReader(downloadResponse.Value.Content))
                    {
                        content = reader.ReadToEnd();
                    }

                    if (!string.IsNullOrEmpty(content))
                    {
                        var result = JsonConvert.DeserializeObject<HostConcurrencySnapshot>(content);
                        return result;
                    }
                }
            }
            catch (RequestFailedException exception) when (exception.Status == 404)
            {
                // we haven't recorded a status yet
                return null;
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Error reading snapshot blob {blobPath}");
                throw e;
            }

            return null;
        }

        public async Task WriteAsync(HostConcurrencySnapshot snapshot, CancellationToken cancellationToken)
        {
            string blobPath = await GetBlobPathAsync(cancellationToken);

            try
            {
                BlobContainerClient? containerClient = await GetContainerClientAsync(cancellationToken);
                if (containerClient != null)
                {
                    BlobClient blobClient = containerClient.GetBlobClient(blobPath);
                    var content = JsonConvert.SerializeObject(snapshot);
                    using (Stream stream = new MemoryStream(Encoding.UTF8.GetBytes(content)))
                    {
                        await blobClient.UploadAsync(stream, overwrite: true);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Error writing snapshot blob {blobPath}");
                throw e;
            }
        }

        internal async Task<BlobContainerClient?> GetContainerClientAsync(CancellationToken cancellationToken)
        {
            if (_blobContainerClient == null)
            {
                if (_azureStorageProvider.TryGetBlobServiceClientFromConnection(out BlobServiceClient blobServiceClient, ConnectionStringNames.Storage))
                {
                    _blobContainerClient = blobServiceClient.GetBlobContainerClient(HostContainerName);

                    await _blobContainerClient.CreateIfNotExistsAsync();
                }
            }

            return _blobContainerClient;
        }

        internal async Task<string> GetBlobPathAsync(CancellationToken cancellationToken)
        {
            string hostId = await _hostIdProvider.GetHostIdAsync(cancellationToken);
            return $"concurrency/{hostId}/concurrencyStatus.json";
        }
    }
}
