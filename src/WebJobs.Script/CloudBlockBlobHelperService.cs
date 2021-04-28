// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Script
{
    public class CloudBlockBlobHelperService
    {
        public virtual bool BlobExists(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return false;
            }

            try
            {
                CloudBlockBlob blob = new CloudBlockBlob(new Uri(url));

                int attempt = 0;
                while (true)
                {
                    try
                    {
                        return blob.Exists();
                    }
                    catch (Exception ex) when (!ex.IsFatal())
                    {
                        if (++attempt > 2)
                        {
                            throw;
                        }
                        Thread.Sleep(TimeSpan.FromSeconds(0.3));
                    }
                }
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}