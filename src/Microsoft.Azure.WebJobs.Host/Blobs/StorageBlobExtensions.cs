// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    internal static class StorageBlobExtensions
    {
        public static string GetBlobPath(this IStorageBlob blob)
        {
            return ToBlobPath(blob).ToString();
        }

        public static BlobPath ToBlobPath(this IStorageBlob blob)
        {
            if (blob == null)
            {
                throw new ArgumentNullException("blob");
            }

            return new BlobPath(blob.Container.Name, blob.Name);
        }

        public static async Task<bool> TryFetchAttributesAsync(this IStorageBlob blob,
            CancellationToken cancellationToken)
        {
            if (blob == null)
            {
                throw new ArgumentNullException("blob");
            }

            try
            {
                await blob.FetchAttributesAsync(cancellationToken);
                return true;
            }
            catch (StorageException exception)
            {
                // Remember specific error codes are not available for Fetch (HEAD request).

                if (exception.IsNotFound())
                {
                    return false;
                }
                else if (exception.IsOk())
                {
                    // If the blob type is incorrect (block vs. page) a 200 OK is returned but the SDK throws an
                    // exception.
                    return false;
                }
                else
                {
                    throw;
                }
            }

        }
    }
}
