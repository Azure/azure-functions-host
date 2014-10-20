// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    internal static class CloudBlobContainerExtensions
    {
        public static async Task<IEnumerable<IListBlobItem>> ListBlobsAsync(this CloudBlobContainer container, 
            bool useFlatBlobListing, CancellationToken cancellationToken)
        {
            if (container == null)
            {
                throw new ArgumentNullException("container");
            }

            List<IListBlobItem> allResults = new List<IListBlobItem>();
            BlobContinuationToken currentToken = null;
            BlobResultSegment result;

            do
            {
                result = await container.ListBlobsSegmentedAsync(prefix: null, useFlatBlobListing: useFlatBlobListing,
                    blobListingDetails: BlobListingDetails.None, maxResults: null, currentToken: currentToken,
                    options: null, operationContext: null, cancellationToken: cancellationToken);

                if (result != null)
                {
                    IEnumerable<IListBlobItem> currentResults = result.Results;

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
