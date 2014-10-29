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
        private readonly IStorageBlobClient _parent;
        private readonly Uri _uri;

        public FakeStorageBlobContainer(MemoryBlobStore store, string containerName, IStorageBlobClient parent)
        {
            _store = store;
            _containerName = containerName;
            _parent = parent;
            _uri = new Uri("http://localhost/" + containerName);
        }

        public string Name
        {
            get { return _containerName; }
        }

        public CloudBlobContainer SdkObject
        {
            get { throw new NotImplementedException(); }
        }

        public IStorageBlobClient ServiceClient
        {
            get { return _parent; }
        }

        public Uri Uri
        {
            get { return _uri; }
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
            IStorageBlob blob = _store.GetBlobReferenceFromServer(this, _containerName, blobName);
            return Task.FromResult(blob);
        }

        public IStorageBlockBlob GetBlockBlobReference(string blobName)
        {
            return new FakeStorageBlockBlob(_store, blobName, this);
        }

        public IStorageBlobDirectory GetDirectoryReference(string relativeAddress)
        {
            return new FakeStorageBlobDirectory(_store, relativeAddress, this);
        }

        public IStoragePageBlob GetPageBlobReference(string blobName)
        {
            return new FakeStoragePageBlob(_store, blobName, this);
        }

        public Task<IStorageBlobResultSegment> ListBlobsSegmentedAsync(string prefix, bool useFlatBlobListing,
            BlobListingDetails blobListingDetails, int? maxResults, BlobContinuationToken currentToken,
            BlobRequestOptions options, OperationContext operationContext, CancellationToken cancellationToken)
        {
            if (options != null)
            {
                throw new NotImplementedException();
            }

            if (operationContext != null)
            {
                throw new NotImplementedException();
            }

            string fullPrefix;

            if (!String.IsNullOrEmpty(prefix))
            {
                fullPrefix = _containerName + "/" + prefix;
            }
            else
            {
                fullPrefix = _containerName;
            }

            Func<string, IStorageBlobContainer> containerFactory = (name) =>
            {
                if (name != _containerName)
                {
                    throw new InvalidOperationException();
                }

                return this;
            };
            IStorageBlobResultSegment segment = _store.ListBlobsSegmented(containerFactory, fullPrefix,
                useFlatBlobListing, blobListingDetails, maxResults, currentToken);
            return Task.FromResult(segment);
        }
    }
}
