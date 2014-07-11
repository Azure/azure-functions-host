using System;
using System.Globalization;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using Microsoft.Azure.Jobs.Storage;
using System.Collections.Generic;

namespace Dashboard.Data.Logs
{
    internal class IndexerBlobLogReader : IIndexerLogReader
    {
        private readonly CloudBlobContainer _logsContainer;

        private readonly string _containerDirectory;

        /// <summary>
        /// Initializes a new instance of the <see cref="BlobLogReader{TEntry}"/> class.
        /// </summary>
        /// <param name="client">The blob client.</param>
        public IndexerBlobLogReader(CloudBlobClient client)
            : this(client, DashboardContainerNames.Dashboard, DashboardDirectoryNames.IndexerLog)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BlobLogReader{TEntry}"/> class.
        /// </summary>
        /// <param name="client">The blob client.</param>
        /// <param name="logsContainer">The logs container.</param>
        /// <param name="containerDirectory">The container directory.</param>
        public IndexerBlobLogReader(CloudBlobClient client, string logsContainer, string containerDirectory)
        {
            if (client == null)
            {
                throw new ArgumentNullException("client");
            }

            if (string.IsNullOrEmpty(logsContainer))
            {
                throw new ArgumentNullException("logsContainer");
            }

            if (containerDirectory == null)
            {
                containerDirectory = string.Empty;
            }

            _logsContainer = client.GetContainerReference(logsContainer);
            _containerDirectory = containerDirectory;
        }

        public IndexerLogEntry ReadWithDetails(string logEntryId)
        {
            string fullBlobName = _containerDirectory + "/" + logEntryId;

            try
            {
                CloudBlockBlob logBlob = _logsContainer.GetBlockBlobReference(fullBlobName);
                string blobContent = logBlob.DownloadText();

                return JsonConvert.DeserializeObject<IndexerLogEntry>(blobContent);
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFound())
                {
                    return null;
                }
                else
                {
                    throw;
                }
            }
        }

        public IResultSegment<IndexerLogEntry> ReadWithoutDetails(int maximumResults, string continuationToken)
        {
            BlobContinuationToken blobContinuationToken = BlobContinuationTokenSerializer.Deserialize(continuationToken);

            BlobResultSegment blobSegment;

            try
            {
                blobSegment = _logsContainer.ListBlobsSegmented(
                    prefix: _containerDirectory,
                    useFlatBlobListing: true,
                    blobListingDetails: BlobListingDetails.Metadata,
                    maxResults: maximumResults,
                    currentToken: blobContinuationToken,
                    options: null,
                    operationContext: null);
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFound())
                {
                    return null;
                }
                else
                {
                    throw;
                }
            }

            if (blobSegment == null)
            {
                return null;
            }

            List<IndexerLogEntry> results = new List<IndexerLogEntry>();

            // Cast from IListBlobItem to ICloudBlob is safe due to useFlatBlobListing: true above.
            foreach (ICloudBlob blob in blobSegment.Results)
            {
                IndexerLogEntry entry = ParseEntryFromBlobMetadata(blob);
                if (entry != null)
                {
                    results.Add(entry);
                }
            }

            string nextContinuationToken = BlobContinuationTokenSerializer.Serialize(blobSegment.ContinuationToken);

            return new ResultSegment<IndexerLogEntry>(results, nextContinuationToken);
        }

        /// <summary>
        /// Creates a log entry without details by only using information
        /// available on the blob itself, without looking at the content
        /// </summary>
        /// <param name="blob">The blob to parse</param>
        /// <returns>A log entry or null</returns>
        private IndexerLogEntry ParseEntryFromBlobMetadata(ICloudBlob blob)
        {
            if (blob == null ||
                !blob.Metadata.ContainsKey(BlobLogEntryKeys.LogDate) ||
                !blob.Metadata.ContainsKey(BlobLogEntryKeys.TitleKey))
            {
                return null;
            }

            IndexerLogEntry entry = new IndexerLogEntry();
            entry.Id = blob.Name;
            if (_containerDirectory.Length != 0 && _containerDirectory.Length < entry.Id.Length)
            {
                entry.Id = entry.Id.Substring(_containerDirectory.Length + 1);
            }

            entry.Date = DateTime.Parse(blob.Metadata[BlobLogEntryKeys.LogDate]);
            entry.Title = blob.Metadata[BlobLogEntryKeys.TitleKey];

            return entry;
        }
    }
}