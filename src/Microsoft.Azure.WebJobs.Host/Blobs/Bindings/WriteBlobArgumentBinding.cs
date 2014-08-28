// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Bindings
{
    internal static class WriteBlobArgumentBinding
    {
        public static async Task<WatchableCloudBlobStream> BindStreamAsync(ICloudBlob blob, ValueBindingContext context)
        {
            CloudBlockBlob blockBlob = blob as CloudBlockBlob;

            if (blockBlob == null)
            {
                throw new InvalidOperationException("Cannot bind a page blob using an out string.");
            }

            BlobCausalityManager.SetWriter(blob.Metadata, context.FunctionInstanceId);

            CloudBlobStream rawStream = await blockBlob.OpenWriteAsync(context.CancellationToken);
            IBlobCommitedAction committedAction = new BlobCommittedAction(blob, context.BlobWrittenWatcher);
            return new WatchableCloudBlobStream(rawStream, committedAction);
        }
    }
}
