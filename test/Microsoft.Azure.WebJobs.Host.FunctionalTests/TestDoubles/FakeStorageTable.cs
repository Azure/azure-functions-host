// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Storage.Table;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles
{
    internal class FakeStorageTable : IStorageTable
    {
        private readonly MemoryTableStore _store;
        private readonly string _tableName;

        public FakeStorageTable(MemoryTableStore store, string tableName)
        {
            _store = store;
            _tableName = tableName;
        }

        public string Name
        {
            get { return _tableName; }
        }

        public IStorageTableBatchOperation CreateBatch()
        {
            return new FakeStorageTableBatchOperation();
        }

        public Task CreateIfNotExistsAsync(CancellationToken cancellationToken)
        {
            _store.CreateIfNotExists(_tableName);
            return Task.FromResult(0);
        }

        public IStorageTableOperation CreateReplaceOperation(ITableEntity entity)
        {
            throw new NotImplementedException();
        }

        public IStorageTableOperation CreateRetrieveOperation<TElement>(string partitionKey, string rowKey)
            where TElement : ITableEntity, new()
        {
            return FakeStorageTableOperation.Retrieve<TElement>(partitionKey, rowKey);
        }

        public Task<TableResult> ExecuteAsync(IStorageTableOperation operation, CancellationToken cancellationToken)
        {
            return Task.FromResult(_store.Execute(_tableName, operation));
        }

        public Task<IList<TableResult>> ExecuteBatchAsync(IStorageTableBatchOperation batch,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_store.ExecuteBatch(_tableName, batch));
        }
    }
}
