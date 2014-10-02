// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Auth;

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
            return new FakeStorageBlobContainer(_store, containerName);
        }
    }
}
