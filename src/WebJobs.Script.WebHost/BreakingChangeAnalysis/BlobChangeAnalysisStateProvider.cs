// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.ChangeAnalysis
{
    internal sealed class BlobChangeAnalysisStateProvider : IChangeAnalysisStateProvider
    {
        public const string AnalysisTimestampMetadataName = "LastAnalysisTime";

        private readonly ILogger _logger;
        private readonly IConfiguration _configuration;
        private readonly IHostIdProvider _hostIdProvider;
        private readonly AzureStorageProvider _azureStorageProvider;

        public BlobChangeAnalysisStateProvider(IConfiguration configuration, IHostIdProvider hostIdProvider, ILogger<BlobChangeAnalysisStateProvider> logger, AzureStorageProvider azureStorageProvider)
        {
            _configuration = configuration;
            _hostIdProvider = hostIdProvider;
            _logger = logger;
            _azureStorageProvider = azureStorageProvider;
        }

        public async Task<ChangeAnalysisState> GetCurrentAsync(CancellationToken cancellationToken)
        {
            if (!_azureStorageProvider.TryGetBlobServiceClientFromConnection(out BlobServiceClient blobServiceClient, ConnectionStringNames.Storage))
            {
                throw new InvalidOperationException($"The {nameof(BlobChangeAnalysisStateProvider)} requires the default storage account '{ConnectionStringNames.Storage}', which is not defined.");
            }

            string hostId = await _hostIdProvider.GetHostIdAsync(cancellationToken);
            string analysisBlobPath = $"changeanalysis/{hostId}/sentinel";

            DateTimeOffset lastAnalysisTime = DateTimeOffset.MinValue;
            BlobClient blobClient = default;
            try
            {
                var blobContainerClient = blobServiceClient.GetBlobContainerClient(ScriptConstants.AzureWebJobsHostsContainerName);
                blobClient = blobContainerClient.GetBlobClient(analysisBlobPath);
                if (await blobClient.ExistsAsync(cancellationToken: cancellationToken))
                {
                    var blobPropertiesResponse = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
                    blobPropertiesResponse.Value.Metadata.TryGetValue(AnalysisTimestampMetadataName, out string lastAnalysisMetadata);

                    _logger.LogInformation("Last analysis flag value '{flag}'.", lastAnalysisMetadata ?? "(null)");

                    if (!DateTimeOffset.TryParse(lastAnalysisMetadata, out lastAnalysisTime))
                    {
                        _logger.LogInformation("Unable to parse last analysis timestamp flag. Default (MinValue) will be used.");
                    }
                }
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Unable to get current change analysis state.");
            }

            return new ChangeAnalysisState(lastAnalysisTime, blobClient);
        }

        public async Task SetTimestampAsync(DateTimeOffset timestamp, object handle, CancellationToken cancellationToken)
        {
            if (handle != null && handle is BlobClient blobClient)
            {
                try
                {
                    if (!await blobClient.ExistsAsync())
                    {
                        using (Stream stream = new MemoryStream())
                        {
                            await blobClient.UploadAsync(stream, cancellationToken: cancellationToken);
                        }
                    }

                    string timestampValue = timestamp.ToString("O");

                    var blobPropertiesResponse = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
                    blobPropertiesResponse.Value.Metadata[AnalysisTimestampMetadataName] = timestampValue;

                    await blobClient.SetMetadataAsync(blobPropertiesResponse.Value.Metadata, cancellationToken: cancellationToken);

                    _logger.LogInformation("Analysis blob metadata updated with analysis timestamp '{timestamp}'.", timestampValue);
                }
                catch (RequestFailedException ex)
                {
                    _logger.LogError(ex, "Unable to set change analysis state metadata.");
                }
            }
            else
            {
                _logger.LogError("Analysis blob SetTimestampAsync was given a null BlobClient");
            }
        }
    }
}
