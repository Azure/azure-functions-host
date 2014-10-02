// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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
    /// <summary>Defines a block blob.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public interface IStorageBlockBlob : IStorageBlob
#else
    internal interface IStorageBlockBlob : IStorageBlob
#endif
    {
        /// <summary>Gets the underlying <see cref="CloudBlockBlob"/>.</summary>
        new CloudBlockBlob SdkObject { get; }

        /// <summary>Opens a stream to write to the blob.</summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A task that will open a stream to write to the blob.</returns>
        Task<CloudBlobStream> OpenWriteAsync(CancellationToken cancellationToken);
    }
}
