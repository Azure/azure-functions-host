// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly CloudTable _sdkObject;

        public FakeStorageTable(MemoryTableStore store, string tableName)
        {
            _store = store;
            _tableName = tableName;
            _sdkObject = new CloudTable(new Uri("http://localhost:10000/fakeaccount/" + tableName));
        }

        public string Name
        {
            get { return _tableName; }
        }

        public CloudTable SdkObject
        {
            get { return _sdkObject; }
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

        public IStorageTableOperation CreateInsertOperation(ITableEntity entity)
        {
            return FakeStorageTableOperation.Insert(entity);
        }

        public IStorageTableOperation CreateInsertOrReplaceOperation(ITableEntity entity)
        {
            throw new NotImplementedException();
        }

        public IQueryable<TElement> CreateQuery<TElement>() where TElement : ITableEntity, new()
        {
            return _store.CreateQuery<TElement>(_tableName);
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

        public Task<bool> ExistsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_store.Exists(_tableName));
        }
    }
}
