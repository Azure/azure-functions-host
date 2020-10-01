// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Script.ChangeAnalysis
{
    internal sealed class BlobChangeAnalysisStateProvider : IChangeAnalysisStateProvider
    {
        public const string AnalysisTimestampMetadataName = "LastAnalysisTime";

        private readonly ILogger _logger;
        private readonly IConfiguration _configuration;
        private readonly IHostIdProvider _hostIdProvider;

        public BlobChangeAnalysisStateProvider(IConfiguration configuration, IHostIdProvider hostIdProvider, ILogger<BlobChangeAnalysisStateProvider> logger)
        {
            _configuration = configuration;
            _hostIdProvider = hostIdProvider;
            _logger = logger;
        }

        public async Task<ChangeAnalysisState> GetCurrentAsync(CancellationToken cancellationToken)
        {
            string storageConnectionString = _configuration.GetWebJobsConnectionString(ConnectionStringNames.Storage);

            if (string.IsNullOrEmpty(storageConnectionString) ||
                !CloudStorageAccount.TryParse(storageConnectionString, out CloudStorageAccount account))
            {
                throw new InvalidOperationException($"The {nameof(BlobChangeAnalysisStateProvider)} requires the default storage account '{ConnectionStringNames.Storage}', which is not defined.");
            }

            string hostId = await _hostIdProvider.GetHostIdAsync(cancellationToken);
            string analysisBlobPath = $"changeanalysis/{hostId}/sentinel";

            CloudBlobClient blobClient = account.CreateCloudBlobClient();
            var blobContainer = blobClient.GetContainerReference(ScriptConstants.AzureWebJobsHostsContainerName);

            DateTimeOffset lastAnalysisTime = DateTimeOffset.MinValue;

            var reference = blobContainer.GetBlockBlobReference(analysisBlobPath);
            bool blobExists = await reference.ExistsAsync(null, null, cancellationToken);
            if (blobExists)
            {
                reference.Metadata.TryGetValue(AnalysisTimestampMetadataName, out string lastAnalysisMetadata);

                _logger.LogInformation("Last analysis flag value '{flag}'.", lastAnalysisMetadata ?? "(null)");

                if (!DateTimeOffset.TryParse(lastAnalysisMetadata, out lastAnalysisTime))
                {
                    _logger.LogInformation("Unable to parse last analysis timestamp flag. Default (MinValue) will be used.");
                }
            }

            return new ChangeAnalysisState(lastAnalysisTime, reference);
        }

        public async Task SetTimestampAsync(DateTimeOffset timestamp, object handle, CancellationToken cancellationToken)
        {
            if (handle is CloudBlockBlob blob)
            {
                if (!await blob.ExistsAsync())
                {
                    await blob.UploadTextAsync(string.Empty);
                }

                string timestampValue = timestamp.ToString("O");
                blob.Metadata[AnalysisTimestampMetadataName] = timestampValue;
                await blob.SetMetadataAsync(null, null, null, cancellationToken);

                _logger.LogInformation("Analysis blob metadata updated with analysis timestamp '{timestamp}'.", timestampValue);
            }
        }
    }
}
