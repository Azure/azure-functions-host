using System;
using System.Collections.Generic;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Protocols
{
    /// <summary>Represents a monitor for running host heartbeats.</summary>
    public class HeartbeatMonitor : IHeartbeatMonitor
    {
        private readonly CloudBlobClient _client;

        /// <summary>Initializes a new instance of the <see cref="HeartbeatMonitor"/> class.</summary>
        /// <param name="client">A blob client for the storage account in which to monitor heartbeats.</param>
        [CLSCompliant(false)]
        public HeartbeatMonitor(CloudBlobClient client)
        {
            _client = client;
        }

        /// <inheritdoc />
        public bool IsSharedHeartbeatValid(string sharedContainerName, string sharedDirectoryName,
            int expirationInSeconds)
        {
            CloudBlobContainer container = _client.GetContainerReference(sharedContainerName);
            CloudBlobDirectory directory = container.GetDirectoryReference(sharedDirectoryName);
            BlobContinuationToken currentToken = null;
            bool foundValidHeartbeat = false;

            do
            {
                BlobResultSegment segment = GetNextHeartbeats(directory, currentToken);

                if (segment == null)
                {
                    return false;
                }

                currentToken = segment.ContinuationToken;
                foundValidHeartbeat = HasValidHeartbeat(segment.Results, expirationInSeconds);
            } while (foundValidHeartbeat == false && currentToken != null);

            return foundValidHeartbeat;
        }

        private BlobResultSegment GetNextHeartbeats(CloudBlobDirectory directory, BlobContinuationToken currentToken)
        {
            const int batchSize = 100;

            try
            {
                return directory.ListBlobsSegmented(useFlatBlobListing: true,
                    blobListingDetails: BlobListingDetails.None,
                    maxResults: batchSize,
                    currentToken: currentToken,
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
        }

        private static bool HasValidHeartbeat(IEnumerable<IListBlobItem> heartbeats, int expirationInSeconds)
        {
            // We're using the flat blob listing, so the more specific ICloudBlob type here is guaranteed.
            foreach (ICloudBlob blob in heartbeats)
            {
                if (HasValidHeartbeat(blob, expirationInSeconds))
                {
                    return true;
                }
                else
                {
                    // Remove any expired heartbeats so that we can answer more efficiently in the future.
                    // If the host instance wakes back up, it will just re-create the heartbeat anyway.
                    blob.DeleteIfExists();
                }
            }

            return false;
        }

        /// <inheritdoc />
        public bool IsInstanceHeartbeatValid(string sharedContainerName, string sharedDirectoryName,
            string instanceBlobName, int expirationInSeconds)
        {
            CloudBlobContainer container = _client.GetContainerReference(sharedContainerName);
            CloudBlobDirectory directory = container.GetDirectoryReference(sharedDirectoryName);
            ICloudBlob blob = directory.GetBlockBlobReference(instanceBlobName);

            try
            {
                blob.FetchAttributes();
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFound())
                {
                    return false;
                }
                else
                {
                    throw;
                }
            }

            return HasValidHeartbeat(blob, expirationInSeconds);
        }

        private static bool HasValidHeartbeat(ICloudBlob heartbeatBlob, int expirationInSeconds)
        {
            // There always should be a value at this point, but defaulting to MinValue just to be safe...
            DateTimeOffset heartbeat = heartbeatBlob.Properties.LastModified.GetValueOrDefault(DateTimeOffset.MinValue);
            return IsValidHeartbeat(heartbeat, expirationInSeconds);
        }

        private static bool IsValidHeartbeat(DateTimeOffset heartbeat, int expirationInSeconds)
        {
            return heartbeat.AddSeconds(expirationInSeconds) > DateTimeOffset.UtcNow;
        }
    }
}
