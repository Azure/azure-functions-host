// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
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
        private readonly CloudBlockBlob _sdkObject;

        public FakeStorageBlockBlob(MemoryBlobStore store, string blobName, IStorageBlobContainer parent)
        {
            _store = store;
            _blobName = blobName;
            _parent = parent;
            _containerName = parent.Name;
            _metadata = new Dictionary<string, string>();
            _sdkObject = new CloudBlockBlob(new Uri("http://localhost/" + _containerName + "/" + blobName));
        }

        public CloudBlockBlob SdkObject
        {
            get { return _sdkObject; }
        }

        public Task<CloudBlobStream> OpenWriteAsync(CancellationToken cancellationToken)
        {
            CloudBlobStream stream = _store.OpenWrite(_containerName, _blobName, _metadata);
            return Task.FromResult(stream);
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
            get { return _metadata; }
        }

        public string Name
        {
            get { return _blobName; }
        }

        ICloudBlob IStorageBlob.SdkObject
        {
            get { throw new NotImplementedException(); }
        }

        public Task<bool> ExistsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_store.Exists(_containerName, _blobName));
        }

        public Task FetchAttributesAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_store.OpenRead(_containerName, _blobName));
        }
    }
}
