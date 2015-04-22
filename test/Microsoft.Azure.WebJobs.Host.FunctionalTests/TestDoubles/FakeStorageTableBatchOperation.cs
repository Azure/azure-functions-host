// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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

        public void Add(IStorageTableOperation operation)
        {
            _items.Add(operation);
        }
    }
}
