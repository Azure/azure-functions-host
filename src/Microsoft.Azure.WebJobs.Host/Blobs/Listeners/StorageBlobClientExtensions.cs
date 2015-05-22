// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    internal static class StorageBlobClientExtensions
    {
        public static async Task<IEnumerable<IStorageListBlobItem>> ListBlobsAsync(this IStorageBlobClient client,
            string prefix, bool useFlatBlobListing, BlobListingDetails blobListingDetails,
            CancellationToken cancellationToken)
        {
            if (client == null)
            {
                throw new ArgumentNullException("client");
            }

            List<IStorageListBlobItem> allResults = new List<IStorageListBlobItem>();
            BlobContinuationToken currentToken = null;
            IStorageBlobResultSegment result;

            do
            {
                result = await client.ListBlobsSegmentedAsync(prefix, useFlatBlobListing, blobListingDetails,
                    maxResults: null, currentToken: currentToken, options: null, operationContext: null,
                    cancellationToken: cancellationToken);

                if (result != null)
                {
                    IEnumerable<IStorageListBlobItem> currentResults = result.Results;

                    if (currentResults != null)
                    {
                        allResults.AddRange(currentResults);
                    }
                }
            } 
            while (result != null && result.ContinuationToken != null);

            return allResults;
        }
    }
}
