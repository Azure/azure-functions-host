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
    /// <summary>Defines a blob container.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public interface IStorageBlobContainer
#else
    internal interface IStorageBlobContainer
#endif
    {
        /// <summary>Gets the name of the container.</summary>
        string Name { get; }

        /// <summary>Gets the underlying <see cref="CloudBlobContainer"/>.</summary>
        CloudBlobContainer SdkObject { get; }

        /// <summary>Gets the URI of the container's primary location.</summary>
        Uri Uri { get; }

        /// <summary>Creates the container if it does not already exist.</summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A task that will create the container if it does not already exist.</returns>
        Task CreateIfNotExistsAsync(CancellationToken cancellationToken);

        /// <summary>Determines whether the container exists.</summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A task that will determine whether the container exists.</returns>
        Task<bool> ExistsAsync(CancellationToken cancellationToken);

        /// <summary>Gets a blob reference from the server.</summary>
        /// <param name="blobName">The blob name.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A task that will get a blob reference from the server.</returns>
        Task<IStorageBlob> GetBlobReferenceFromServerAsync(string blobName, CancellationToken cancellationToken);

        /// <summary>Gets a block blob reference.</summary>
        /// <param name="blobName">The blob name.</param>
        /// <returns>A block blob reference.</returns>
        IStorageBlockBlob GetBlockBlobReference(string blobName);

        /// <summary>Gets a page blob reference.</summary>
        /// <param name="blobName">The blob name.</param>
        /// <returns>A page blob reference.</returns>
        IStoragePageBlob GetPageBlobReference(string blobName);
    }
}
