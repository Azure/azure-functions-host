// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
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
    /// <summary>Represents a page blob.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public class StoragePageBlob : IStoragePageBlob
#else
    internal class StoragePageBlob : IStoragePageBlob
#endif
    {
        private readonly IStorageBlobContainer _parent;
        private readonly CloudPageBlob _sdk;
        private readonly IStorageBlobProperties _properties;

        /// <summary>Initializes a new instance of the <see cref="StoragePageBlob"/> class.</summary>
        /// <param name="parent">The parent blob container.</param>
        /// <param name="sdk">The SDK blob to wrap.</param>
        public StoragePageBlob(IStorageBlobContainer parent, CloudPageBlob sdk)
        {
            _parent = parent;
            _sdk = sdk;
            _properties = new StorageBlobProperties(sdk);
        }

        /// <inheritdoc />
        public StorageBlobType BlobType
        {
            get { return StorageBlobType.PageBlob; }
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
        public CloudPageBlob SdkObject
        {
            get { return _sdk; }
        }

        /// <inheritdoc />
        public Task<string> AcquireLeaseAsync(TimeSpan? leaseTime, string proposedLeaseId,
            CancellationToken cancellationToken)
        {
            return _sdk.AcquireLeaseAsync(leaseTime, proposedLeaseId, cancellationToken);
        }

        /// <inheritdoc />
        public Task DeleteAsync(CancellationToken cancellationToken)
        {
            return _sdk.DeleteAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task<bool> ExistsAsync(CancellationToken cancellationToken)
        {
            return _sdk.ExistsAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task FetchAttributesAsync(CancellationToken cancellationToken)
        {
            return _sdk.FetchAttributesAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
        {
            return _sdk.OpenReadAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task<CloudBlobStream> OpenWriteAsync(long? size, CancellationToken cancellationToken)
        {
            return _sdk.OpenWriteAsync(size, cancellationToken);
        }

        /// <inheritdoc />
        public Task ReleaseLeaseAsync(AccessCondition accessCondition, BlobRequestOptions options,
            OperationContext operationContext, CancellationToken cancellationToken)
        {
            return _sdk.ReleaseLeaseAsync(accessCondition, options, operationContext, cancellationToken);
        }

        /// <inheritdoc />
        public Task SetMetadataAsync(AccessCondition accessCondition, BlobRequestOptions options,
            OperationContext operationContext, CancellationToken cancellationToken)
        {
            return _sdk.SetMetadataAsync(accessCondition, options, operationContext, cancellationToken);
        }
    }
}
