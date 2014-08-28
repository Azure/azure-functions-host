// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    internal static class ReadBlobArgumentBinding
    {
        public static async Task<WatchableReadStream> BindStreamAsync(ICloudBlob blob, ValueBindingContext context)
        {
            Stream rawStream = await blob.OpenReadAsync(context.CancellationToken);
            return new WatchableReadStream(rawStream);
        }

        public static TextReader CreateTextReader(WatchableReadStream watchableStream)
        {
            return new StreamReader(watchableStream);
        }
    }
}
