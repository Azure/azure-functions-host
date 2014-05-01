using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Azure.Jobs;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Microsoft.WindowsAzure.Storage.Table.Queryable;

namespace AzureTables
{
    // Connects an AzureTable class to a live azure table. 
    class LiveTableCore : TableCore
    {
        private readonly CloudStorageAccount _account;
        private readonly string _tableName;

        private readonly CloudTableClient _client;
        private readonly CloudTable _table;

        public LiveTableCore(CloudStorageAccount account, string tableName)
        {
            _account = account;
            _tableName = tableName;

            _client = _account.CreateCloudTableClient();
            _table = _client.GetTableReference(tableName);

            // This can fail if the table was recently deleted and is still in the process of being deleted.
            _table.CreateIfNotExists();
        }

        public override ITableCorePartitionWriter NewPartitionWriter(string partitionKey)
        {
            return new AzureTableWriteCorePartition(_table, partitionKey);
        }

        class AzureTableWriteCorePartition : ITableCorePartitionWriter
        {
            private readonly CloudTable _table;
            private readonly string _partitionKey;
            private readonly TableBatchOperation _batch;

            public AzureTableWriteCorePartition(CloudTable table, string partitionKey)
            {
                _table = table;
                _partitionKey = partitionKey;
                _batch = new TableBatchOperation();
            }

            void ITableCorePartitionWriter.Flush()
            {
                _table.ExecuteBatch(_batch);
                _batch.Clear();
            }

            void ITableCorePartitionWriter.AddObject(DynamicTableEntity entity)
            {
                if (entity.PartitionKey != _partitionKey)
                {
                    // Caller should ensure this doesn't happen. 
                    throw new InvalidOperationException("Partition key mismatch");
                }

                _batch.InsertOrReplace(entity);
            }
        }

        public override void DeleteTable()
        {
            // This could take minutes.
            TableClient.DeleteTable(_account, _tableName);
        }

        public override void DeleteTableAsync()
        {
            // This could take a while for delete to be finished            
            _table.DeleteIfExists();
        }

        public override void DeleteTablePartition(string partitionKey)
        {
            TableClient.DeleteTablePartition(_account, _tableName, partitionKey);
        }

        public override void DeleteTableRow(string partitionKey, string rowKey)
        {
            TableClient.DeleteTableRow(_account, _tableName, partitionKey, rowKey);
        }

        public override IEnumerable<DynamicTableEntity> Enumerate(string partitionKey)
        {
            // Azure will special case this lookup pattern for a single entity. 
            // See http://blogs.msdn.com/b/windowsazurestorage/archive/2010/11/06/how-to-get-most-out-of-windows-azure-tables.aspx 
            try
            {

                IQueryable<DynamicTableEntity> query;
                if (partitionKey == null)
                {
                    query = from o in _table.CreateQuery<DynamicTableEntity>()
                            select o;
                }
                else
                {
                    query = from o in _table.CreateQuery<DynamicTableEntity>()
                            where o.PartitionKey == partitionKey
                            select o;
                }
                return query.AsTableQuery().Execute();
            }
            catch (StorageException)
            {
                // Not found. 
                return null;
            }
        }

        [DebuggerNonUserCode]
        public override DynamicTableEntity Lookup(string partitionKey, string rowKey)
        {
            // Azure will special case this lookup pattern for a single entity. 
            // See http://blogs.msdn.com/b/windowsazurestorage/archive/2010/11/06/how-to-get-most-out-of-windows-azure-tables.aspx 
            try
            {
                var x = from o in _table.CreateQuery<DynamicTableEntity>()
                        where o.PartitionKey == partitionKey && o.RowKey == rowKey
                        select o;
                return x.FirstOrDefault();
            }
            catch (StorageException)
            {
                // Not found. 
                return null;
            }
        }
    }
}
