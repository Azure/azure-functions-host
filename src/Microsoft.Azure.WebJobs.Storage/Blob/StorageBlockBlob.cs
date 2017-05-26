// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Blob
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Blob
#endif
{
    /// <summary>Represents a block blob.</summary>
#if PUBLICSTORAGE
    
    public class StorageBlockBlob : IStorageBlockBlob
#else
    internal class StorageBlockBlob : IStorageBlockBlob
#endif
    {
        private readonly IStorageBlobContainer _parent;
        private readonly CloudBlockBlob _sdk;
        private readonly IStorageBlobProperties _properties;

        /// <summary>Initializes a new instance of the <see cref="StorageBlockBlob"/> class.</summary>
        /// <param name="parent">The parent blob container.</param>
        /// <param name="sdk">The SDK blob to wrap.</param>
        public StorageBlockBlob(IStorageBlobContainer parent, CloudBlockBlob sdk)
        {
            _parent = parent;
            _sdk = sdk;
            _properties = new StorageBlobProperties(sdk);
        }

        /// <inheritdoc />
        public StorageBlobType BlobType
        {
            get { return StorageBlobType.BlockBlob; }
        }

        /// <inheritdoc />
        public IStorageBlobContainer Container
        {
            get { return _parent; }
        }

        /// <inheritdoc />
        public IDictionary<string, string> Metadata
        {
            get { return _sdk.Metadata; }
        }

        /// <inheritdoc />
        public Uri Uri
        {
            get { return _sdk.Uri; }
        }

        /// <inheritdoc />
        public string Name
        {
            get { return _sdk.Name; }
        }

        /// <inheritdoc />
        public IStorageBlobProperties Properties
        {
            get { return _properties; }
        }

        /// <inheritdoc />
        ICloudBlob IStorageBlob.SdkObject
        {
            get { return _sdk; }
        }

        /// <inheritdoc />
        public CloudBlockBlob SdkObject
        {
            get { return _sdk; }
        }

        /// <inheritdoc />
        public Task<string> AcquireLeaseAsync(TimeSpan? leaseTime, string proposedLeaseId,
            CancellationToken cancellationToken)
        {
            return _sdk.AcquireLeaseAsync(leaseTime, proposedLeaseId, accessCondition: null, options: null, operationContext: null, cancellationToken: cancellationToken);
        }

        /// <inheritdoc />
        public Task DeleteAsync(CancellationToken cancellationToken)
        {
            return _sdk.DeleteAsync(DeleteSnapshotsOption.None, accessCondition: null, options: null, operationContext: null, cancellationToken: cancellationToken);
        }

        /// <inheritdoc />
        public Task<string> DownloadTextAsync(CancellationToken cancellationToken)
        {
            return _sdk.DownloadTextAsync(encoding: null, accessCondition: null, options: null, operationContext: null, cancellationToken: cancellationToken);
        }

        /// <inheritdoc />
        public Task<bool> ExistsAsync(CancellationToken cancellationToken)
        {
            return _sdk.ExistsAsync(options: null, operationContext: null, cancellationToken: cancellationToken);
        }

        /// <inheritdoc />
        public Task FetchAttributesAsync(CancellationToken cancellationToken)
        {
            return _sdk.FetchAttributesAsync(accessCondition: null, options: null, operationContext: null, cancellationToken: cancellationToken);
        }

        /// <inheritdoc />
        public Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
        {
            return _sdk.OpenReadAsync(accessCondition: null, options: null, operationContext: null, cancellationToken: cancellationToken);
        }

        /// <inheritdoc />
        public Task<CloudBlobStream> OpenWriteAsync(CancellationToken cancellationToken)
        {
            return _sdk.OpenWriteAsync(accessCondition: null, options: null, operationContext: null, cancellationToken: cancellationToken);
        }

        /// <inheritdoc />
        public Task ReleaseLeaseAsync(AccessCondition accessCondition, BlobRequestOptions options,
            OperationContext operationContext, CancellationToken cancellationToken)
        {
            return _sdk.ReleaseLeaseAsync(accessCondition, options, operationContext, cancellationToken);
        }

        /// <inheritdoc />
        public Task RenewLeaseAsync(AccessCondition accessCondition, BlobRequestOptions options,
            OperationContext operationContext, CancellationToken cancellationToken)
        {
            return _sdk.RenewLeaseAsync(accessCondition, options, operationContext, cancellationToken);
        } 

        /// <inheritdoc />
        public Task SetMetadataAsync(AccessCondition accessCondition, BlobRequestOptions options,
            OperationContext operationContext, CancellationToken cancellationToken)
        {
            return _sdk.SetMetadataAsync(accessCondition, options, operationContext, cancellationToken);
        }

        /// <inheritdoc />
        public Task UploadTextAsync(string content, Encoding encoding, AccessCondition accessCondition,
            BlobRequestOptions options, OperationContext operationContext, CancellationToken cancellationToken)
        {
            return _sdk.UploadTextAsync(content, encoding, accessCondition, options, operationContext,
                cancellationToken);
        }
    }
}
