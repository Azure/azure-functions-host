using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Storage;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class RecentInvocationIndexReader : IRecentInvocationIndexReader
    {
        private readonly CloudBlobContainer _container;

        [CLSCompliant(false)]
        public RecentInvocationIndexReader(CloudBlobClient client)
            : this (client.GetContainerReference(DashboardContainerNames.RecentFunctionsContainerName))
        {
        }

        [CLSCompliant(false)]
        public RecentInvocationIndexReader(CloudBlobContainer container)
        {
            _container = container;
        }

        public IResultSegment<RecentInvocationEntry> Read(int maximumResults, string continuationToken)
        {
            return Read(_container, DashboardBlobPrefixes.Flat, maximumResults, continuationToken);
        }

        internal static IResultSegment<RecentInvocationEntry> Read(CloudBlobContainer container, string prefix,
            int maximumResults, string continuationToken)
        {
            BlobContinuationToken blobContinuationToken = BlobContinuationTokenSerializer.Deserialize(continuationToken);

            BlobResultSegment blobSegment;

            try
            {
                blobSegment = container.ListBlobsSegmented(
                    prefix: prefix,
                    useFlatBlobListing: true,
                    blobListingDetails: BlobListingDetails.None,
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

            List<RecentInvocationEntry> results = new List<RecentInvocationEntry>();

            // Cast from IListBlobItem to ICloudBlob is safe due to useFlatBlobListing: true above.
            foreach (ICloudBlob blob in blobSegment.Results)
            {
                string nameWithoutPrefix = blob.Name.Substring(prefix.Length);
                results.Add(RecentInvocationEntry.Parse(nameWithoutPrefix));
            }

            string nextContinuationToken = BlobContinuationTokenSerializer.Serialize(blobSegment.ContinuationToken);

            return new ResultSegment<RecentInvocationEntry>(results, nextContinuationToken);
        }
    }
}
