// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host.Storage.Table;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles
{
    internal class FakeStorageTableBatchOperation : IStorageTableBatchOperation
    {
        private readonly List<IStorageTableOperation> _items = new List<IStorageTableOperation>();

        public int Count
        {
            get { return _items.Count; }
        }

        public IStorageTableOperation this[int index]
        {
            get { return _items[index]; }
        }

        public IEnumerator<IStorageTableOperation> GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)_items).GetEnumerator();
        }

        public void Insert(ITableEntity entity)
        {
            _items.Add(FakeStorageTableOperation.Insert(entity));
        }
    }
}
