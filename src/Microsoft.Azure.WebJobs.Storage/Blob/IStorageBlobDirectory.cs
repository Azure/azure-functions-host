// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Blob
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Blob
#endif
{
    /// <summary>Defines a blob directory.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public interface IStorageBlobDirectory : IStorageListBlobItem
#else
    internal interface IStorageBlobDirectory : IStorageListBlobItem
#endif
    {
        /// <summary>Gets the container for the blob.</summary>
        IStorageBlobContainer Container { get; }

        /// <summary>Gets the blob service client.</summary>
        IStorageBlobClient ServiceClient { get; }

        /// <summary>Gets a block blob reference.</summary>
        /// <param name="blobName">The blob name.</param>
        /// <returns>A block blob reference.</returns>
        IStorageBlockBlob GetBlockBlobReference(string blobName);
    }
}
