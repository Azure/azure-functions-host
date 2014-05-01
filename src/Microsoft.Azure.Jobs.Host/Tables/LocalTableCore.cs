using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs;
using Microsoft.WindowsAzure.Storage.Table;

namespace AzureTables
{
    // Simulate azure table primitize in-memory
    class LocalTableCore : TableCore
    {
        class FakePartition : ITableCorePartitionWriter
        {
            private readonly string _partitionKey;
            Dictionary<string, DynamicTableEntity> _rows = new Dictionary<string, DynamicTableEntity>();

            public FakePartition(string partitionKey)
            {
                _partitionKey = partitionKey;
            }

            public void Delete(string rowKey)
            {
                _rows.Remove(rowKey);
            }

            public IEnumerable<DynamicTableEntity> Enumerate()
            {
                foreach (var kv in _rows)
                {
                    yield return kv.Value;
                }
            }

            public DynamicTableEntity Lookup(string rowKey)
            {
                DynamicTableEntity value;
                if (_rows.TryGetValue(rowKey, out value))
                {
                    return value;
                }
                return null;
            }

            void ITableCorePartitionWriter.AddObject(DynamicTableEntity entity)
            {
                // Azure always adds an automatic property for the timestamp.
                entity.Timestamp = DateTimeOffset.UtcNow;
                _rows[entity.RowKey] = entity;
            }

            void ITableCorePartitionWriter.Flush()
            {
                // Nop.                
            }
        }

        Dictionary<string, FakePartition> _table = new Dictionary<string, FakePartition>();

        public override void DeleteTable()
        {
            _table.Clear();
        }

        public override void DeleteTableAsync()
        {
            _table.Clear();
        }

        public override void DeleteTablePartition(string partitionKey)
        {
            _table.Remove(partitionKey);
        }

        public override void DeleteTableRow(string partitionKey, string rowKey)
        {
            FakePartition part;
            if (_table.TryGetValue(partitionKey, out part))
            {
                part.Delete(rowKey);
            }
        }

        public override IEnumerable<DynamicTableEntity> Enumerate(string partitionKey)
        {
            if (partitionKey == null)
            {
                foreach (var part in _table)
                {
                    foreach (var entry in part.Value.Enumerate())
                    {
                        yield return entry;
                    }
                }

            }
            else
            {
                FakePartition part;
                if (_table.TryGetValue(partitionKey, out part))
                {
                    foreach (var entry in part.Enumerate())
                    {
                        yield return entry;
                    }
                }
            }
        }

        public override DynamicTableEntity Lookup(string partitionKey, string rowKey)
        {
            FakePartition part;
            if (_table.TryGetValue(partitionKey, out part))
            {
                return part.Lookup(rowKey);
            }
            return null;
        }

        public override ITableCorePartitionWriter NewPartitionWriter(string partitionKey)
        {
            FakePartition part;
            if (!_table.TryGetValue(partitionKey, out part))
            {
                part = new FakePartition(partitionKey);
                _table[partitionKey] = part;
            }
            return part;
        }
    }
}
