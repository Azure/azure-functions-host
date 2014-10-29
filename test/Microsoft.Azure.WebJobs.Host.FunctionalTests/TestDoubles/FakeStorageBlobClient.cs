// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Shared.Protocol;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles
{
    internal class FakeStorageBlobClient : IStorageBlobClient
    {
        private readonly MemoryBlobStore _store;
        private readonly StorageCredentials _credentials;

        public FakeStorageBlobClient(MemoryBlobStore store, StorageCredentials credentials)
        {
            _store = store;
            _credentials = credentials;
        }

        public StorageCredentials Credentials
        {
            get { return _credentials; }
        }

        public IStorageBlobContainer GetContainerReference(string containerName)
        {
            return new FakeStorageBlobContainer(_store, containerName, this);
        }

        public Task<ServiceProperties> GetServicePropertiesAsync(CancellationToken cancellationToken)
        {
            ServiceProperties properties = _store.GetServiceProperties();
            return Task.FromResult(properties);
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

            Func<string, IStorageBlobContainer> containerFactory = (n) => new FakeStorageBlobContainer(_store, n, this);
            IStorageBlobResultSegment segment = _store.ListBlobsSegmented(containerFactory, prefix, useFlatBlobListing,
                blobListingDetails, maxResults, currentToken);
            return Task.FromResult(segment);
        }

        public Task SetServicePropertiesAsync(ServiceProperties properties, CancellationToken cancellationToken)
        {
            _store.SetServiceProperties(properties);
            return Task.FromResult(0);
        }
    }
}
