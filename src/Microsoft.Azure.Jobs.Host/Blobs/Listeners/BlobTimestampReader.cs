// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    internal class BlobTimestampReader : IBlobTimestampReader
    {
        private static readonly BlobTimestampReader _instance = new BlobTimestampReader();

        private BlobTimestampReader()
        {
        }

        public static BlobTimestampReader Instance
        {
            get { return _instance; }
        }

        public async Task<DateTime?> GetLastModifiedTimestampAsync(ICloudBlob blob, CancellationToken cancellationToken)
        {
            try
            {
                await blob.FetchAttributesAsync(cancellationToken);
            }
            catch (StorageException exception)
            {
                // Blob may not exist or be a different blob type (block vs. page).
                if (exception.IsNotFound() || exception.IsConflict())
                {
                    return null;
                }
                else
                {
                    throw;
                }
            }

            DateTimeOffset? lastModified = blob.Properties.LastModified;

            if (!lastModified.HasValue)
            {
                // Can this happen after FetchAttributes succeeds?
                return null;
            }

            return lastModified.Value.UtcDateTime;
        }
    }
}
