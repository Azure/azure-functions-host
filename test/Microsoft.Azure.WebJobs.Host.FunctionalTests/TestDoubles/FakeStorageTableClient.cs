// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Storage.Table;
using Microsoft.WindowsAzure.Storage.Auth;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles
{
    internal class FakeStorageTableClient : IStorageTableClient
    {
        private readonly MemoryTableStore _store;
        private readonly StorageCredentials _credentials;

        public FakeStorageTableClient(MemoryTableStore store, StorageCredentials credentials)
        {
            _store = store;
            _credentials = credentials;
        }

        public StorageCredentials Credentials
        {
            get { return _credentials; }
        }

        public IStorageTable GetTableReference(string tableName)
        {
            return new FakeStorageTable(_store, tableName);
        }
    }
}
