using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Storage;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class RecentFunctionReader : IRecentFunctionReader
    {
        private readonly CloudBlobContainer _container;

        [CLSCompliant(false)]
        public RecentFunctionReader(CloudBlobClient client)
            : this (client.GetContainerReference(DashboardContainerNames.RecentFunctionsContainerName))
        {
        }

        [CLSCompliant(false)]
        public RecentFunctionReader(CloudBlobContainer container)
        {
            _container = container;
        }

        public IResultSegment<RecentFunctionInstance> Read(int maximumResults, string continuationToken)
        {
            BlobContinuationToken blobContinuationToken = BlobContinuationTokenSerializer.Deserialize(continuationToken);

            BlobResultSegment blobSegment;

            try
            {
                blobSegment = _container.ListBlobsSegmented(
                    prefix: null,
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

            List<RecentFunctionInstance> results = new List<RecentFunctionInstance>();

            // Cast from IListBlobItem to ICloudBlob is safe due to useFlatBlobListing: true above.
            foreach (ICloudBlob blob in blobSegment.Results)
            {
                results.Add(RecentFunctionInstance.Parse(blob.Name));
            }

            string nextContinuationToken = BlobContinuationTokenSerializer.Serialize(blobSegment.ContinuationToken);

            return new ResultSegment<RecentFunctionInstance>(results, nextContinuationToken);
        }
    }
}
