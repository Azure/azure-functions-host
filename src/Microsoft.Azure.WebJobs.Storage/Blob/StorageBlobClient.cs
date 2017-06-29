// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// <summary>Represents a blob client.</summary>
#if PUBLICSTORAGE
    
    public class StorageBlobClient : IStorageBlobClient
#else
    internal class StorageBlobClient : IStorageBlobClient
#endif
    {
        private readonly CloudBlobClient _sdk;

        /// <summary>Initializes a new instance of the <see cref="StorageBlobClient"/> class.</summary>
        /// <param name="sdk">The SDK client to wrap.</param>
        public StorageBlobClient(CloudBlobClient sdk)
        {
            _sdk = sdk;
        }

        /// <inheritdoc />
        public StorageCredentials Credentials
        {
            get { return _sdk.Credentials; }
        }

        /// <inheritdoc />
        public IStorageBlobContainer GetContainerReference(string containerName)
        {
            CloudBlobContainer sdkContainer = _sdk.GetContainerReference(containerName);
            return new StorageBlobContainer(this, sdkContainer);
        }

        /// <inheritdoc />
        public Task<ServiceProperties> GetServicePropertiesAsync(CancellationToken cancellationToken)
        {
            return _sdk.GetServicePropertiesAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task<IStorageBlobResultSegment> ListBlobsSegmentedAsync(string prefix, bool useFlatBlobListing,
            BlobListingDetails blobListingDetails, int? maxResults, BlobContinuationToken currentToken,
            BlobRequestOptions options, OperationContext operationContext, CancellationToken cancellationToken)
        {
            Task<BlobResultSegment> sdkTask = _sdk.ListBlobsSegmentedAsync(prefix, useFlatBlobListing,
                blobListingDetails, maxResults, currentToken, options, operationContext, cancellationToken);
            return ListBlobsSegmentedAsyncCore(sdkTask);
        }

        private async Task<IStorageBlobResultSegment> ListBlobsSegmentedAsyncCore(Task<BlobResultSegment> sdkTask)
        {
            BlobResultSegment sdkSegment = await sdkTask;

            if (sdkSegment == null)
            {
                return null;
            }

            IEnumerable<IListBlobItem> sdkResults = sdkSegment.Results;

            List<IStorageListBlobItem> results;

            if (sdkResults != null)
            {
                results = new List<IStorageListBlobItem>();

                foreach (IListBlobItem sdkResult in sdkResults)
                {
                    CloudBlobContainer sdkContainer = sdkResult.Container;
                    Debug.Assert(sdkContainer != null);
                    IStorageBlobContainer container = new StorageBlobContainer(this, sdkContainer);
                    IStorageListBlobItem result = StorageBlobContainer.ToStorageListBlobItem(container, sdkResult);
                    results.Add(result);
                }
            }
            else
            {
                results = null;
            }

            return new StorageBlobResultSegment(sdkSegment.ContinuationToken, results);
        }

        /// <inheritdoc />
        public Task SetServicePropertiesAsync(ServiceProperties properties, CancellationToken cancellationToken)
        {
            return _sdk.SetServicePropertiesAsync(properties, cancellationToken);
        }
    }
}
