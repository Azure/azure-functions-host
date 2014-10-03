// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.WindowsAzure.Storage.Table;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Table
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Table
#endif
{
    /// <summary>Represents an atomic batch operation on a table.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public class StorageTableBatchOperation : IStorageTableBatchOperation
#else
    internal class StorageTableBatchOperation : IStorageTableBatchOperation
#endif
    {
        private readonly TableBatchOperation _sdk;
        private readonly List<IStorageTableOperation> _items;

        /// <summary>Initializes a new instance of the <see cref="StorageTableBatchOperation"/> class.</summary>
        /// <param name="sdk">The SDK batch operation to wrap.</param>
        public StorageTableBatchOperation(TableBatchOperation sdk)
        {
            _sdk = sdk;
            _items = new List<IStorageTableOperation>();
        }

        /// <inheritdoc />
        public int Count
        {
            get { return _items.Count; }
        }

        /// <inheritdoc />
        public IStorageTableOperation this[int index]
        {
            get { return _items[index]; }
        }

        /// <summary>Gets the underlying <see cref="TableBatchOperation"/>.</summary>
        public TableBatchOperation SdkObject
        {
            get { return _sdk; }
        }

        /// <inheritdoc />
        public IEnumerator<IStorageTableOperation> GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)_items).GetEnumerator();
        }

        /// <inheritdoc />
        public void Add(IStorageTableOperation operation)
        {
            TableOperation sdkOperation = ((StorageTableOperation)operation).SdkObject;
            _sdk.Add(sdkOperation);
            _items.Add(operation);
        }
    }
}
