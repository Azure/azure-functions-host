// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.Azure.WebJobs.Host.Storage;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    internal static class ReadBlobArgumentBinding
    {
        public static async Task<WatchableReadStream> TryBindStreamAsync(ICloudBlob blob, ValueBindingContext context)
        {
            Stream rawStream;
            try
            {
                rawStream = await blob.OpenReadAsync(context.CancellationToken);
            }
            catch (StorageException exception)
            {
                // Testing generic error case since specific error codes are not available for FetchAttributes 
                // (HEAD request), including OpenRead. 
                if (!exception.IsNotFound())
                {
                    throw;
                }

                return null;
            }
            
            return new WatchableReadStream(rawStream);
        }

        public static TextReader CreateTextReader(WatchableReadStream watchableStream)
        {
            return new StreamReader(watchableStream);
        }
    }
}
