using System;
using System.Collections.Generic;
using Microsoft.WindowsAzure.Jobs;

namespace AzureTables
{
    // Simulate azure table primitize in-memory
    class LocalTableCore : TableCore
    {
        class FakePartition : ITableCorePartitionWriter
        {
            private readonly string _partitionKey;
            Dictionary<string, IDictionary<string, string>> _rows = new Dictionary<string, IDictionary<string, string>>();

            public FakePartition(string partitionKey)
            {
                _partitionKey = partitionKey;
            }

            public void Delete(string rowKey)
            {
                _rows.Remove(rowKey);
            }

            public IEnumerable<GenericEntity> Enumerate()
            {
                foreach (var kv in _rows)
                {
                    yield return new GenericEntity
                    {
                        PartitionKey = _partitionKey,
                        RowKey = kv.Key,
                        properties = kv.Value
                    };
                }
            }

            public GenericEntity Lookup(string rowKey)
            {
                IDictionary<string, string> values;
                if (_rows.TryGetValue(rowKey, out values))
                {
                    return new GenericEntity
                    {
                        PartitionKey = _partitionKey,
                        RowKey = rowKey,
                        properties = values
                    };
                }
                return null;
            }

            void ITableCorePartitionWriter.AddObject(GenericEntity entity)
            {
                // Azure always adds an automatic property for the timestamp.
                string timeStamp = ObjectBinderHelpers.SerializeDateTime(DateTime.UtcNow);
                entity.properties["Timestamp"] = timeStamp;
                _rows[entity.RowKey] = entity.properties;
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

        public override IEnumerable<GenericEntity> Enumerate(string partitionKey)
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

        public override GenericEntity Lookup(string partitionKey, string rowKey)
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
