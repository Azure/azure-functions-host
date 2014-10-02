// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Blob;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Blob
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Blob
#endif
{
    /// <summary>Defines a blob.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public interface IStorageBlob
#else
    internal interface IStorageBlob
#endif
    {
        /// <summary>Gets the type of the blob.</summary>
        StorageBlobType BlobType { get; }

        /// <summary>Gets the container for the blob.</summary>
        IStorageBlobContainer Container { get; }

        /// <summary>Gets the user-defined metadata for the blob.</summary>
        IDictionary<string, string> Metadata { get; }

        /// <summary>Gets the name of the blob.</summary>
        string Name { get; }

        /// <summary>Gets the underlying <see cref="ICloudBlob"/>.</summary>
        ICloudBlob SdkObject { get; }

        /// <summary>Determines whether the blob exists.</summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A task that will determine whether the blob exists.</returns>
        Task<bool> ExistsAsync(CancellationToken cancellationToken);

        /// <summary>Populates the blob's properties and metadata.</summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A task that will populate the blob's properties and metadata.</returns>
        Task FetchAttributesAsync(CancellationToken cancellationToken);

        /// <summary>Opens a stream to read from the blob.</summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A task that will open a stream to read from the blob.</returns>
        Task<Stream> OpenReadAsync(CancellationToken cancellationToken);
    }
}
