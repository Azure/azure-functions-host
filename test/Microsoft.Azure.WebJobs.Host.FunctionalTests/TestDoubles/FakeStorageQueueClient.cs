// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Auth;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles
{
    internal class FakeStorageQueueClient : IStorageQueueClient
    {
        private readonly MemoryQueueStore _store;
        private readonly StorageCredentials _credentials;

        public FakeStorageQueueClient(MemoryQueueStore store, StorageCredentials credentials)
        {
            _store = store;
            _credentials = credentials;
        }

        public StorageCredentials Credentials
        {
            get { return _credentials; }
        }

        public IStorageQueue GetQueueReference(string queueName)
        {
            return new FakeStorageQueue(_store, queueName, this);
        }
    }
}
