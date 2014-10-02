// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles
{
    internal class MemoryBlobStore
    {
        private readonly ConcurrentDictionary<string, Container> _items = new ConcurrentDictionary<string, Container>();

        public void CreateIfNotExists(string containerName)
        {
            _items.AddOrUpdate(containerName, new Container(), (_, existing) => existing);
        }

        public bool Exists(string containerName)
        {
            return _items.ContainsKey(containerName);
        }

        public bool Exists(string containerName, string blobName)
        {
            return _items[containerName].Exists(blobName);
        }

        public Stream OpenRead(string containerName, string blobName)
        {
            return _items[containerName].OpenRead(blobName);
        }

        public CloudBlobStream OpenWrite(string containerName, string blobName, IDictionary<string, string> metadata)
        {
            return _items[containerName].OpenWrite(blobName, metadata);
        }

        private class Container
        {
            private readonly ConcurrentDictionary<string, Blob> _items = new ConcurrentDictionary<string, Blob>();

            public bool Exists(string blobName)
            {
                return _items.ContainsKey(blobName);
            }

            public Stream OpenRead(string blobName)
            {
                return new MemoryStream(_items[blobName].Contents, writable: false);
            }

            public CloudBlobStream OpenWrite(string blobName, IDictionary<string, string> metadata)
            {
                return new MemoryCloudBlobStream((bytes) => _items[blobName] = new Blob(bytes, metadata));
            }
        }

        private class Blob
        {
            private readonly IReadOnlyDictionary<string, string> _metadata;
            private readonly byte[] _contents;

            public Blob(byte[] contents, IDictionary<string, string> metadata)
            {
                _contents = new byte[contents.LongLength];
                Array.Copy(contents, _contents, _contents.LongLength);
                _metadata = new Dictionary<string, string>(metadata);
            }

            public byte[] Contents
            {
                get { return _contents; }
            }
        }
    }
}
