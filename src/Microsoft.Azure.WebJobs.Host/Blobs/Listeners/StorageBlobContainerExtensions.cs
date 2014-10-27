// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    internal static class StorageBlobContainerExtensions
    {
        public static async Task<IEnumerable<IStorageListBlobItem>> ListBlobsAsync(this IStorageBlobContainer container, 
            bool useFlatBlobListing, CancellationToken cancellationToken)
        {
            if (container == null)
            {
                throw new ArgumentNullException("container");
            }

            List<IStorageListBlobItem> allResults = new List<IStorageListBlobItem>();
            BlobContinuationToken currentToken = null;
            IStorageBlobResultSegment result;

            do
            {
                result = await container.ListBlobsSegmentedAsync(prefix: null, useFlatBlobListing: useFlatBlobListing,
                    blobListingDetails: BlobListingDetails.None, maxResults: null, currentToken: currentToken,
                    options: null, operationContext: null, cancellationToken: cancellationToken);

                if (result != null)
                {
                    IEnumerable<IStorageListBlobItem> currentResults = result.Results;

                    if (currentResults != null)
                    {
                        allResults.AddRange(currentResults);
                    }
                }

            } while (result != null && result.ContinuationToken != null);

            return allResults;
        }
    }
}
