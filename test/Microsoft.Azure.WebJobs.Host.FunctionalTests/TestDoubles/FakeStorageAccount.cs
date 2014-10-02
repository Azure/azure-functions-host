// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.Storage.Table;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles
{
    internal class FakeStorageAccount : IStorageAccount
    {
        private static readonly StorageCredentials _credentials = new StorageCredentials();

        private readonly MemoryBlobStore _blobStore = new MemoryBlobStore();
        private readonly MemoryQueueStore _queueStore = new MemoryQueueStore();

        public StorageCredentials Credentials
        {
            get { return _credentials; }
        }

        public CloudStorageAccount SdkObject
        {
            get { throw new NotImplementedException(); }
        }

        public IStorageBlobClient CreateBlobClient()
        {
            return new FakeStorageBlobClient(_blobStore, _credentials);
        }

        public IStorageQueueClient CreateQueueClient()
        {
            return new FakeStorageQueueClient(_queueStore, _credentials);
        }

        public IStorageTableClient CreateTableClient()
        {
            throw new NotImplementedException();
        }

        public string ToString(bool exportSecrets)
        {
            throw new NotImplementedException();
        }
    }
}
