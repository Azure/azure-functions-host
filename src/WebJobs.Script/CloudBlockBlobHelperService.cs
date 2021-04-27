// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Script
{
    public class CloudBlockBlobHelperService
    {
        public virtual async Task<bool> BlobExists(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return false;
            }
            return await BlobExistsAsync(url);
        }

        private static async Task<bool> BlobExistsAsync(string url)
        {
            bool exists = false;
            await Utility.InvokeWithRetriesAsync(async () =>
            {
                try
                {
                    CloudBlockBlob blob = new CloudBlockBlob(new Uri(url));
                    exists = await blob.ExistsAsync();
                }
                catch (Exception)
                {
                    throw;
                }
            }, maxRetries: 2, retryInterval: TimeSpan.FromSeconds(0.3));

            return exists;
        }
    }
}