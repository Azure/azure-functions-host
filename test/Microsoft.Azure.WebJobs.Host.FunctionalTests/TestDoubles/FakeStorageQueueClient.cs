// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles
{
    internal class FakeStorageQueueClient : IStorageQueueClient
    {
        private readonly MemoryQueueStore _store;
        private readonly StorageCredentials _credentials;
        private readonly CloudQueueClient _sdk;

        public FakeStorageQueueClient(MemoryQueueStore store, StorageCredentials credentials)
        {
            _store = store;
            _credentials = credentials;
            _sdk = new CloudQueueClient(new Uri("http://localhost/"), credentials);
        }

        public StorageCredentials Credentials
        {
            get { return _credentials; }
        }

        public IStorageQueue GetQueueReference(string queueName)
        {
            return new FakeStorageQueue(_store, queueName, this);
        }

        public CloudQueueClient SdkObject
        {
            get
            {
                return _sdk;
            }
        }
    }
}
