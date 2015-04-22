// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Bindings
{
    internal static class WriteBlobArgumentBinding
    {
        public static async Task<WatchableCloudBlobStream> BindStreamAsync(IStorageBlob blob,
            ValueBindingContext context, IBlobWrittenWatcher blobWrittenWatcher)
        {
            IStorageBlockBlob blockBlob = blob as IStorageBlockBlob;

            if (blockBlob == null)
            {
                throw new InvalidOperationException("Cannot bind a page blob using an out string.");
            }

            BlobCausalityManager.SetWriter(blob.Metadata, context.FunctionInstanceId);

            CloudBlobStream rawStream = await blockBlob.OpenWriteAsync(context.CancellationToken);
            IBlobCommitedAction committedAction = new BlobCommittedAction(blob, blobWrittenWatcher);
            return new WatchableCloudBlobStream(rawStream, committedAction);
        }
    }
}
