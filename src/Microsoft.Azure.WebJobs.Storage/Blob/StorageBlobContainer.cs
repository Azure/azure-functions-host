// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
#if PUBLICSTORAGE
using Microsoft.Azure.WebJobs.Storage.Blob;
#else
#endif
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Blob
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Blob
#endif
{
    /// <summary>Represents a blob container.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public class StorageBlobContainer : IStorageBlobContainer
#else
    internal class StorageBlobContainer : IStorageBlobContainer
#endif
    {
        private readonly IStorageBlobClient _parent;
        private readonly CloudBlobContainer _sdk;

        /// <summary>Initializes a new instance of the <see cref="StorageBlobContainer"/> class.</summary>
        /// <param name="parent">The parent blob client.</param>
        /// <param name="sdk">The SDK container to wrap.</param>
        public StorageBlobContainer(IStorageBlobClient parent, CloudBlobContainer sdk)
        {
            _parent = parent;
            _sdk = sdk;
        }

        /// <inheritdoc />
        public string Name
        {
            get { return _sdk.Name; }
        }

        /// <inheritdoc />
        public CloudBlobContainer SdkObject
        {
            get { return _sdk; }
        }

        /// <inheritdoc />
        public IStorageBlobClient ServiceClient
        {
            get { return _parent; }
        }

        /// <inheritdoc />
        public Uri Uri
        {
            get { return _sdk.Uri; }
        }

        /// <inheritdoc />
        public Task CreateIfNotExistsAsync(CancellationToken cancellationToken)
        {
            return _sdk.CreateIfNotExistsAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task<bool> ExistsAsync(CancellationToken cancellationToken)
        {
            return _sdk.ExistsAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task<IStorageBlob> GetBlobReferenceFromServerAsync(string blobName, CancellationToken cancellationToken)
        {
            Task<ICloudBlob> sdkTask = _sdk.GetBlobReferenceFromServerAsync(blobName, cancellationToken);
            return GetBlobReferenceFromServerAsyncCore(sdkTask);
        }

        private async Task<IStorageBlob> GetBlobReferenceFromServerAsyncCore(Task<ICloudBlob> sdkTask)
        {
            ICloudBlob sdkBlob = await sdkTask;

            if (sdkBlob == null)
            {
                return null;
            }

            return ToStorageBlob(this, sdkBlob);
        }

        /// <inheritdoc />
        public IStorageBlockBlob GetBlockBlobReference(string blobName)
        {
            CloudBlockBlob sdkBlob = _sdk.GetBlockBlobReference(blobName);
            return new StorageBlockBlob(this, sdkBlob);
        }

        /// <inheritdoc />
        public IStorageBlobDirectory GetDirectoryReference(string relativeAddress)
        {
            CloudBlobDirectory sdkDirectory = _sdk.GetDirectoryReference(relativeAddress);
            return new StorageBlobDirectory(this, sdkDirectory);
        }

        /// <inheritdoc />
        public IStoragePageBlob GetPageBlobReference(string blobName)
        {
            CloudPageBlob sdkBlob = _sdk.GetPageBlobReference(blobName);
            return new StoragePageBlob(this, sdkBlob);
        }

        /// <inheritdoc />
        public IStorageAppendBlob GetAppendBlobReference(string blobName)
        {
            CloudAppendBlob sdkBlob = _sdk.GetAppendBlobReference(blobName);
            return new StorageAppendBlob(this, sdkBlob);
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
                    IStorageListBlobItem result = ToStorageListBlobItem(this, sdkResult);
                    results.Add(result);
                }
            }
            else
            {
                results = null;
            }

            return new StorageBlobResultSegment(sdkSegment.ContinuationToken, results);
        }

        internal static IStorageListBlobItem ToStorageListBlobItem(IStorageBlobContainer parent, IListBlobItem sdkItem)
        {
            if (sdkItem == null)
            {
                return null;
            }

            ICloudBlob sdkBlob = sdkItem as ICloudBlob;

            if (sdkBlob != null)
            {
                return ToStorageBlob(parent, sdkBlob);
            }
            else
            {
                return new StorageBlobDirectory(parent, (CloudBlobDirectory)sdkItem);
            }
        }

        private static IStorageBlob ToStorageBlob(IStorageBlobContainer parent, ICloudBlob sdkBlob)
        {
            switch (sdkBlob.BlobType)
            {
                case BlobType.PageBlob:
                    return new StoragePageBlob(parent, (CloudPageBlob)sdkBlob);
                case BlobType.BlockBlob:
                    return new StorageBlockBlob(parent, (CloudBlockBlob)sdkBlob);
                case BlobType.AppendBlob:
                    return new StorageAppendBlob(parent, (CloudAppendBlob)sdkBlob);
                default:
                    throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "Blob '{0}' has an unsupported type: '{1}'.", sdkBlob.Name, sdkBlob.BlobType));
            }
        }
    }
}
