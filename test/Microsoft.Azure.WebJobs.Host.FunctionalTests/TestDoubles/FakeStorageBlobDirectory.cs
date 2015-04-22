// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles
{
    internal class FakeStorageBlobDirectory : IStorageBlobDirectory
    {
        private readonly MemoryBlobStore _store;
        private readonly string _relativeAddress;
        private readonly IStorageBlobContainer _parent;

        public FakeStorageBlobDirectory(MemoryBlobStore store, string relativeAddress, IStorageBlobContainer parent)
        {
            _store = store;
            _relativeAddress = relativeAddress;
            _parent = parent;
        }

        public IStorageBlobContainer Container
        {
            get { throw new NotImplementedException(); }
        }

        public IStorageBlobClient ServiceClient
        {
            get { throw new NotImplementedException(); }
        }

        public IStorageBlockBlob GetBlockBlobReference(string blobName)
        {
            return new FakeStorageBlockBlob(_store, _relativeAddress + "/" + blobName, _parent);
        }
    }
}
