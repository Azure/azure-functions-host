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
    internal class FakeStorageBlockBlob : IStorageBlockBlob
    {
        private readonly MemoryBlobStore _store;
        private readonly string _blobName;
        private readonly IStorageBlobContainer _parent;
        private readonly string _containerName;
        private readonly IDictionary<string, string> _metadata;
        private readonly FakeStorageBlobProperties _properties;
        private readonly CloudBlockBlob _sdkObject;

        public FakeStorageBlockBlob(MemoryBlobStore store, string blobName, IStorageBlobContainer parent, FakeStorageBlobProperties properties = null)
        {
            _store = store;
            _blobName = blobName;
            _parent = parent;
            _containerName = parent.Name;
            _metadata = new Dictionary<string, string>();
            if (properties != null)
            {
                _properties = properties;
            }
            else
            {
                _properties = new FakeStorageBlobProperties();
            }
            _sdkObject = new CloudBlockBlob(new Uri("http://localhost/" + _containerName + "/" + blobName));
        }

        public StorageBlobType BlobType
        {
            get { return StorageBlobType.BlockBlob; }
        }

        public IStorageBlobContainer Container
        {
            get { return _parent; }
        }

        public IDictionary<string, string> Metadata
        {
            get { return _metadata; }
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

        public CloudBlockBlob SdkObject
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
            if (proposedLeaseId != null)
            {
                throw new NotImplementedException();
            }

            string leaseId = _store.AcquireLease(_containerName, _blobName, leaseTime);
            return Task.FromResult(leaseId);
        }

        public Task DeleteAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public async Task<string> DownloadTextAsync(CancellationToken cancellationToken)
        {
            using (Stream stream = await OpenReadAsync(CancellationToken.None))
            {
                using (TextReader reader = new StreamReader(stream, StrictEncodings.Utf8))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        public Task<bool> ExistsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_store.Exists(_containerName, _blobName));
        }

        public Task FetchAttributesAsync(CancellationToken cancellationToken)
        {
            BlobAttributes attributes = _store.FetchAttributes(_containerName, _blobName);
            _properties.ETag = attributes.ETag;
            _properties.LastModified = attributes.LastModified;
            _metadata.Clear();

            foreach (KeyValuePair<string, string> item in attributes.Metadata)
            {
                _metadata.Add(item);
            }

            return Task.FromResult(0);
        }

        public Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_store.OpenRead(_containerName, _blobName));
        }

        public Task<CloudBlobStream> OpenWriteAsync(CancellationToken cancellationToken)
        {
            CloudBlobStream stream = _store.OpenWriteBlock(_containerName, _blobName, _metadata);
            return Task.FromResult(stream);
        }

        public Task ReleaseLeaseAsync(AccessCondition accessCondition, BlobRequestOptions options,
            OperationContext operationContext, CancellationToken cancellationToken)
        {
            if (accessCondition == null)
            {
                throw new ArgumentNullException("accessCondition");
            }

            if (options != null)
            {
                throw new NotImplementedException();
            }

            if (operationContext != null)
            {
                throw new NotImplementedException();
            }

            if (accessCondition.IfMatchETag != null ||
                accessCondition.IfModifiedSinceTime.HasValue ||
                accessCondition.IfNoneMatchETag != null ||
                accessCondition.IfNotModifiedSinceTime.HasValue ||
                accessCondition.IfSequenceNumberEqual.HasValue ||
                accessCondition.IfSequenceNumberLessThan.HasValue ||
                accessCondition.IfSequenceNumberLessThanOrEqual.HasValue)
            {
                throw new NotImplementedException();
            }

            _store.ReleaseLease(_containerName, _blobName, accessCondition.LeaseId);
            return Task.FromResult(0);
        }

        public Task RenewLeaseAsync(AccessCondition accessCondition, BlobRequestOptions options,
            OperationContext operationContext, CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }

        public Task SetMetadataAsync(AccessCondition accessCondition, BlobRequestOptions options,
            OperationContext operationContext, CancellationToken cancellationToken)
        {
            if (options != null)
            {
                throw new NotImplementedException();
            }

            if (operationContext != null)
            {
                throw new NotImplementedException();
            }

            if (accessCondition != null &&
                (accessCondition.IfMatchETag != null ||
                accessCondition.IfModifiedSinceTime.HasValue ||
                accessCondition.IfNoneMatchETag != null ||
                accessCondition.IfNotModifiedSinceTime.HasValue ||
                accessCondition.IfSequenceNumberEqual.HasValue ||
                accessCondition.IfSequenceNumberLessThan.HasValue ||
                accessCondition.IfSequenceNumberLessThanOrEqual.HasValue))
            {
                throw new NotImplementedException();
            }

            string leaseId;

            if (accessCondition != null)
            {
                leaseId = accessCondition.LeaseId;
            }
            else
            {
                leaseId = null;
            }

            _store.SetMetadata(_containerName, _blobName, _metadata, leaseId);
            return Task.FromResult(0);
        }

        public Task UploadTextAsync(string content, Encoding encoding, AccessCondition accessCondition,
            BlobRequestOptions options, OperationContext operationContext, CancellationToken cancellationToken)
        {
            using (CloudBlobStream stream = _store.OpenWriteBlock(_containerName, _blobName, _metadata))
            {
                byte[] buffer = Encoding.UTF8.GetBytes(content);
                stream.Write(buffer, 0, buffer.Length);
                stream.Commit();
            }

            return Task.FromResult(0);
        }
    }
}
