// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Storage;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class BlobRecentInvocationIndexReader : IBlobRecentInvocationIndexReader
    {
        private readonly CloudBlobContainer _container;
        private readonly string _directoryPrefix;

        [CLSCompliant(false)]
        public BlobRecentInvocationIndexReader(CloudBlobClient client, string directoryName)
            : this (client.GetContainerReference(DashboardContainerNames.Dashboard)
                .GetDirectoryReference(directoryName))
        {
        }

        private BlobRecentInvocationIndexReader(CloudBlobDirectory directory)
        {
            _container = directory.Container;
            _directoryPrefix = directory.Prefix;
        }

        public IResultSegment<RecentInvocationEntry> Read(string relativePrefix, int maximumResults, string continuationToken)
        {
            BlobContinuationToken blobContinuationToken = BlobContinuationTokenSerializer.Deserialize(continuationToken);

            BlobResultSegment blobSegment;
            string prefix = _directoryPrefix + relativePrefix;

            try
            {
                blobSegment = _container.ListBlobsSegmented(
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
