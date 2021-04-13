// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management.LinuxSpecialization
{
    public class RunFromPackageCloudBlockBlobService
    {
        public virtual async Task<bool> BlobExists(string url, string environmentVariableName, ILogger logger)
        {
            return await BlobExistsAsync(url, environmentVariableName, logger);
        }

        private static async Task<bool> BlobExistsAsync(string url, string environmentVariableName, ILogger logger)
        {
            bool exists = false;
            await Utility.InvokeWithRetriesAsync(async () =>
            {
                try
                {
                    CloudBlockBlob blob = new CloudBlockBlob(new Uri(url));
                    exists = await blob.ExistsAsync();
                }
                catch (Exception e)
                {
                    logger.LogError(e, $"Failed to check if zip url blob exists");
                    throw;
                }
            }, maxRetries: 2, retryInterval: TimeSpan.FromSeconds(0.3));

            if (!exists)
            {
                logger.LogWarning($"{environmentVariableName} points to an empty location. Function app has no content.");
            }

            return exists;
        }
    }
}