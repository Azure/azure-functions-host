// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles
{
    internal class FakeStorageAppendBlob : IStorageAppendBlob
    {
        private readonly MemoryBlobStore _store;
        private readonly string _blobName;
        private readonly IStorageBlobContainer _parent;
        private readonly string _containerName;
        private readonly IDictionary<string, string> _metadata;
        private readonly CloudAppendBlob _sdkObject;
        private readonly StorageBlobProperties _properties;

        public FakeStorageAppendBlob(MemoryBlobStore store, string blobName, IStorageBlobContainer parent)
        {
            _store = store;
            _blobName = blobName;
            _parent = parent;
            _containerName = parent.Name;
            _metadata = new Dictionary<string, string>();
            _sdkObject = new CloudAppendBlob(new Uri("http://localhost/" + _containerName + "/" + blobName));
            _properties = new StorageBlobProperties(_sdkObject);
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
            get { return _sdkObject.Metadata; }
        }

        /// <inheritdoc />
        public Uri Uri
        {
            get { return _sdkObject.Uri; }
        }

        public string Name
        {
            get { return _blobName; }
        }

        public IStorageBlobProperties Properties
        {
            get { return _properties; }
        }

        public CloudAppendBlob SdkObject
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

        public Task ReleaseLeaseAsync(AccessCondition accessCondition, BlobRequestOptions options,
            OperationContext operationContext, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task RenewLeaseAsync(AccessCondition accessCondition, BlobRequestOptions options,
            OperationContext operationContext, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task SetMetadataAsync(AccessCondition accessCondition, BlobRequestOptions options,
            OperationContext operationContext, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task UploadTextAsync(string content, Encoding encoding = null, AccessCondition accessCondition = null, BlobRequestOptions options = null, OperationContext operationContext = null, CancellationToken cancellationToken = default(CancellationToken))
        {
             using (CloudBlobStream stream = _store.OpenWriteAppend(_containerName, _blobName, _metadata))
            {
                byte[] buffer = Encoding.UTF8.GetBytes(content);
                stream.Write(buffer, 0, buffer.Length);
                stream.CommitAsync().Wait();
            }

            return Task.FromResult(0);
        }
    }
}
