// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class HostAssignmentZipUrl
    {
        public HostAssignmentZipUrl(string envVar, string url)
        {
            EnvVar = envVar;
            Url = url;
        }

        public string EnvVar { get; set; }

        public string Url { get; set; }

        public bool IsScmRunFromPackage()
        {
            return string.Equals(EnvVar, EnvironmentSettingNames.ScmRunFromPackage,
                        StringComparison.OrdinalIgnoreCase);
        }

        public async Task<bool> BlobExistsAsync(ILogger logger)
        {
            bool exists = false;
            await Utility.InvokeWithRetriesAsync(async () =>
            {
                try
                {
                    CloudBlockBlob blob = new CloudBlockBlob(new Uri(Url));
                    exists = await blob.ExistsAsync();
                }
                catch (Exception e)
                {
                    logger.LogError(e, $"Failed to check if zip url blob exists");
                    throw;
                }
            }, maxRetries: 2, retryInterval: TimeSpan.FromSeconds(0.3));

            return exists;
        }

        public bool IsValidBlobReference()
        {
            try
            {
                new CloudBlockBlob(new Uri(Url));
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }
    }
}
