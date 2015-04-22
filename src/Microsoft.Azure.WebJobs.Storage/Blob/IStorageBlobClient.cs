// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Shared.Protocol;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Blob
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Blob
#endif
{
    /// <summary>Defines a blob client.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public interface IStorageBlobClient
#else
    internal interface IStorageBlobClient
#endif
    {
        /// <summary>Gets the credentials used to connect to the account.</summary>
        StorageCredentials Credentials { get; }

        /// <summary>Gets a container reference.</summary>
        /// <param name="containerName">The container name.</param>
        /// <returns>A container reference.</returns>
        IStorageBlobContainer GetContainerReference(string containerName);

        /// <summary>Gets the service's properties.</summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A task that will retrieve the services's properties.</returns>
        Task<ServiceProperties> GetServicePropertiesAsync(CancellationToken cancellationToken);

        /// <summary>Gets a segment of blobs in a container.</summary>
        /// <param name="prefix">The blob name prefix.</param>
        /// <param name="useFlatBlobListing">
        /// Whether to use a flat listing rather than a hierarchical one by virtual directory.
        /// </param>
        /// <param name="blobListingDetails">The details to include in the listing.</param>
        /// <param name="maxResults">A limit on the number of results to be returned.</param>
        /// <param name="currentToken">A continuation token indicating where to resume listing.</param>
        /// <param name="options">The options for the request.</param>
        /// <param name="operationContext">The operation context for the request.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A task that will get a segment of blobs in a container.</returns>
        Task<IStorageBlobResultSegment> ListBlobsSegmentedAsync(string prefix, bool useFlatBlobListing,
            BlobListingDetails blobListingDetails, int? maxResults, BlobContinuationToken currentToken,
            BlobRequestOptions options, OperationContext operationContext, CancellationToken cancellationToken);

        /// <summary>Sets the service's properties.</summary>
        /// <param name="properties">The service properties to set.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A task that will retrieve the services's properties.</returns>
        Task SetServicePropertiesAsync(ServiceProperties properties, CancellationToken cancellationToken);
    }
}
