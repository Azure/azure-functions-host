// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    internal interface IBlobReceiptManager
    {
        CloudBlockBlob CreateReference(string hostId, string functionId, string containerName, string blobName,
            string eTag);

        Task<BlobReceipt> TryReadAsync(CloudBlockBlob blob, CancellationToken cancellationToken);

        Task<bool> TryCreateAsync(CloudBlockBlob blob, CancellationToken cancellationToken);

        Task<string> TryAcquireLeaseAsync(CloudBlockBlob blob, CancellationToken cancellationToken);

        Task MarkCompletedAsync(CloudBlockBlob blob, string leaseId, CancellationToken cancellationToken);

        Task ReleaseLeaseAsync(CloudBlockBlob blob, string leaseId, CancellationToken cancellationToken);
    }
}
