// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles
{
    internal class FakeStoragePageBlob : IStoragePageBlob
    {
        private readonly MemoryBlobStore _store;
        private readonly string _blobName;
        private readonly IStorageBlobContainer _parent;
        private readonly string _containerName;
        private readonly IDictionary<string, string> _metadata;
        private readonly CloudPageBlob _sdkObject;

        public FakeStoragePageBlob(MemoryBlobStore store, string blobName, IStorageBlobContainer parent)
        {
            _store = store;
            _blobName = blobName;
            _parent = parent;
            _containerName = parent.Name;
            _metadata = new Dictionary<string, string>();
            _sdkObject = new CloudPageBlob(new Uri("http://localhost/" + _containerName + "/" + blobName));
        }

        public StorageBlobType BlobType
        {
            get { throw new NotImplementedException(); }
        }

        public IStorageBlobContainer Container
        {
            get { return _parent; }
        }

        public IDictionary<string, string> Metadata
        {
            get { throw new NotImplementedException(); }
        }

        public string Name
        {
            get { return _blobName; }
        }

        public IStorageBlobProperties Properties
        {
            get { throw new NotImplementedException(); }
        }

        public CloudPageBlob SdkObject
        {
            get { return _sdkObject; }
        }

        ICloudBlob IStorageBlob.SdkObject
        {
            get { return _sdkObject; }
        }

        public Task<string> AcquireLeaseAsync(TimeSpan? leaseTime, string proposedLeaseId,
            CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task DeleteAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<bool> ExistsAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task FetchAttributesAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<CloudBlobStream> OpenWriteAsync(long? size, CancellationToken cancellationToken)
        {
            CloudBlobStream stream = _store.OpenWritePage(_containerName, _blobName, size, _metadata);
            return Task.FromResult(stream);
        }

        public Task ReleaseLeaseAsync(AccessCondition accessCondition, BlobRequestOptions options,
            OperationContext operationContext, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task SetMetadataAsync(AccessCondition accessCondition, BlobRequestOptions options,
            OperationContext operationContext, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
