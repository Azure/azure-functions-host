// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
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
        private readonly CloudBlobContainer _sdk;

        /// <summary>Initializes a new instance of the <see cref="CloudBlobContainer"/> class.</summary>
        /// <param name="sdk">The SDK container to wrap.</param>
        public StorageBlobContainer(CloudBlobContainer sdk)
        {
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

            CloudBlockBlob blockBlob = sdkBlob as CloudBlockBlob;

            if (blockBlob != null)
            {
                return new StorageBlockBlob(this, blockBlob);
            }
            else
            {
                Debug.Assert(sdkBlob is CloudPageBlob);
                return new StoragePageBlob(this, (CloudPageBlob)sdkBlob);
            }
        }

        /// <inheritdoc />
        public IStorageBlockBlob GetBlockBlobReference(string blobName)
        {
            CloudBlockBlob sdkBlob = _sdk.GetBlockBlobReference(blobName);
            return new StorageBlockBlob(this, sdkBlob);
        }

        /// <inheritdoc />
        public IStoragePageBlob GetPageBlobReference(string blobName)
        {
            CloudPageBlob sdkBlob = _sdk.GetPageBlobReference(blobName);
            return new StoragePageBlob(this, sdkBlob);
        }
    }
}
