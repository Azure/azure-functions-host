// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Storage;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    internal static class CloudBlobExtensions
    {
        public static bool TryFetchAttributes(this ICloudBlob blob)
        {
            if (blob == null)
            {
                throw new ArgumentNullException("blob");
            }

            try
            {
                blob.FetchAttributes();
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
