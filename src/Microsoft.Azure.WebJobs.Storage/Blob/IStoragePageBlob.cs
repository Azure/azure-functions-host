// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Blob;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Blob
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Blob
#endif
{
    /// <summary>Defines a page blob.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public interface IStoragePageBlob : IStorageBlob
#else
    internal interface IStoragePageBlob : IStorageBlob
#endif
    {
        /// <summary>Gets the underlying <see cref="CloudPageBlob"/>.</summary>
        new CloudPageBlob SdkObject { get; }

        /// <summary>Opens a stream to write to the blob.</summary>
        /// <param name="size">The size of the page blob, in bytes (must be a multiple of 512).</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A task that will open a stream to write to the blob.</returns>
        Task<CloudBlobStream> OpenWriteAsync(long? size, CancellationToken cancellationToken);
    }
}
