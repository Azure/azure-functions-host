// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    internal class BlobETagReader : IBlobETagReader
    {
        private static readonly BlobETagReader _instance = new BlobETagReader();

        private BlobETagReader()
        {
        }

        public static BlobETagReader Instance
        {
            get { return _instance; }
        }

        public async Task<string> GetETagAsync(ICloudBlob blob, CancellationToken cancellationToken)
        {
            try
            {
                await blob.FetchAttributesAsync(cancellationToken);
            }
            catch (StorageException exception)
            {
                // Note that specific exception codes are not available for FetchAttributes, which makes a HEAD request.

                if (exception.IsNotFound())
                {
                    // Blob does not exist.
                    return null;
                }
                else if (exception.IsOk())
                {
                    // If the blob type is incorrect (block vs. page) a 200 OK is returned but the SDK throws an
                    // exception.
                    return null;
                }
                else
                {
                    throw;
                }
            }

            return blob.Properties.ETag;
        }
    }
}
