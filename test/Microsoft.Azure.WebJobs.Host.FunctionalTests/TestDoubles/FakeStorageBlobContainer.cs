// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles
{
    internal class FakeStorageBlobContainer : IStorageBlobContainer
    {
        private readonly MemoryBlobStore _store;
        private readonly string _containerName;

        public FakeStorageBlobContainer(MemoryBlobStore store, string containerName)
        {
            _store = store;
            _containerName = containerName;
        }

        public string Name
        {
            get { return _containerName; }
        }

        public CloudBlobContainer SdkObject
        {
            get { throw new NotImplementedException(); }
        }

        public Uri Uri
        {
            get { throw new NotImplementedException(); }
        }

        public Task CreateIfNotExistsAsync(CancellationToken cancellationToken)
        {
            _store.CreateIfNotExists(_containerName);
            return Task.FromResult(0);
        }

        public Task<bool> ExistsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_store.Exists(_containerName));
        }

        public Task<IStorageBlob> GetBlobReferenceFromServerAsync(string blobName, CancellationToken cancellationToken)
        {
            if (!_store.Exists(_containerName, blobName))
            {
                throw StorageExceptionFactory.Create(404);
            }

            IStorageBlob blob = new FakeStorageBlockBlob(_store, blobName, this);
            return Task.FromResult(blob);
        }

        public IStorageBlockBlob GetBlockBlobReference(string blobName)
        {
            return new FakeStorageBlockBlob(_store, blobName, this);
        }

        public IStoragePageBlob GetPageBlobReference(string blobName)
        {
            throw new NotImplementedException();
        }
    }
}
