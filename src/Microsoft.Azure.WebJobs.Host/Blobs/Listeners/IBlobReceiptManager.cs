// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    internal interface IBlobReceiptManager
    {
        IStorageBlockBlob CreateReference(string hostId, string functionId, string containerName, string blobName,
            string eTag);

        Task<BlobReceipt> TryReadAsync(IStorageBlockBlob blob, CancellationToken cancellationToken);

        Task<bool> TryCreateAsync(IStorageBlockBlob blob, CancellationToken cancellationToken);

        Task<string> TryAcquireLeaseAsync(IStorageBlockBlob blob, CancellationToken cancellationToken);

        Task MarkCompletedAsync(IStorageBlockBlob blob, string leaseId, CancellationToken cancellationToken);

        Task ReleaseLeaseAsync(IStorageBlockBlob blob, string leaseId, CancellationToken cancellationToken);
    }
}
