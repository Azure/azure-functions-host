// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Azure.WebJobs.Host.Storage.Table;
using Microsoft.Azure.WebJobs.Host.Tables;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles
{
    internal class MemoryTableStore
    {
        private readonly ConcurrentDictionary<string, Table> _items = new ConcurrentDictionary<string, Table>();

        public void CreateIfNotExists(string tableName)
        {
            _items.AddOrUpdate(tableName, new Table(), (_, existing) => existing);
        }

        public TableResult Execute(string tableName, IStorageTableOperation operation)
        {
            return _items[tableName].Execute(operation);
        }

        public IList<TableResult> ExecuteBatch(string tableName, IStorageTableBatchOperation batch)
        {
            return _items[tableName].ExecuteBatch(batch);
        }

        private class Table
        {
            private readonly ConcurrentDictionary<Tuple<string, string>, TableItem> _entities =
                new ConcurrentDictionary<Tuple<string, string>, TableItem>();

            public TableResult Execute(IStorageTableOperation operation)
            {
                TableOperationType operationType = operation.OperationType;
                ITableEntity entity = operation.Entity;

                switch (operation.OperationType)
                {
                    case TableOperationType.Retrieve:
                        TableItem item = _entities[new Tuple<string, string>(operation.RetrievePartitionKey,
                            operation.RetrieveRowKey)];
                        return new TableResult
                        {
                            Result = operation.RetrieveEntityResolver.Resolve(operation.RetrievePartitionKey,
                                operation.RetrieveRowKey, item.Timestamp, item.CloneProperties(), eTag: null)
                        };

                    case TableOperationType.Insert:
                        if (!_entities.TryAdd(new Tuple<string, string>(entity.PartitionKey, entity.RowKey),
                            new TableItem(entity.WriteEntity(operationContext: null))))
                        {
                            throw new InvalidOperationException(String.Format(CultureInfo.CurrentCulture,
                                "Entity PK='{0}',RK='{1}' already exists.", entity.PartitionKey, entity.RowKey));
                        }
                        return new TableResult();

                    default:
                        throw new InvalidOperationException(
                            "Unsupported operation type " + operationType.ToString());
                }
            }

            public IList<TableResult> ExecuteBatch(IStorageTableBatchOperation batch)
            {
                IStorageTableOperation[] operations = batch.ToArray();

                if (operations.Length == 0)
                {
                    return new List<TableResult>();
                }

                // Ensure all operations are for the same partition.
                string firstPartitionKey = operations[0].Entity.PartitionKey;

                if (operations.Any(o => o.Entity.PartitionKey != firstPartitionKey))
                {
                    throw new InvalidOperationException("All operations in a batch must have the same partition key.");
                }

                // Ensure each row key is only present once.
                if (operations.GroupBy(o => o.Entity.RowKey).Any(g => g.Count() > 1))
                {
                    throw new InvalidOperationException("All operations in a batch must have distinct row keys.");
                }

                List<TableResult> results = new List<TableResult>();

                foreach (IStorageTableOperation operation in operations)
                {
                    // For test purposes, use an easier (non-atomic) implementation.
                    TableResult result = Execute(operation);
                    results.Add(result);
                }

                return results;
            }
        }

        private class TableItem
        {
            private readonly IDictionary<string, EntityProperty> _properties = new Dictionary<string, EntityProperty>();
            private DateTimeOffset _timestamp;

            public TableItem(IDictionary<string, EntityProperty> properties)
            {
                // Clone properties when persisting, so changes to source value in memory don't affect the persisted
                // data.
                _properties = TableEntityValueBinder.DeepClone(properties);
                _timestamp = DateTimeOffset.Now;
            }

            public DateTimeOffset Timestamp
            {
                get { return _timestamp; }
            }

            public IDictionary<string, EntityProperty> CloneProperties()
            {
                // Clone properties when retrieving, so changes to the retrieved value in memory don't affect the
                // persisted data.
                return TableEntityValueBinder.DeepClone(_properties);
            }
        }
    }
}
