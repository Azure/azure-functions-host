using System;
using System.Collections.Generic;
using System.Data.Services.Client;
using System.Data.Services.Common;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;
using SimpleBatch;

namespace AzureTables
{
    // Constants for writing the XML schema for Azure Tables.
    class AzureTableConstants
    {
        public static XNamespace AtomNamespace = "http://www.w3.org/2005/Atom";
        public static XNamespace DataNamespace = "http://schemas.microsoft.com/ado/2007/08/dataservices";
        public static XNamespace MetadataNamespace = "http://schemas.microsoft.com/ado/2007/08/dataservices/metadata";
    }
    
    // Connects an AzureTable class to a live azure table. 
    class LiveTableCore : TableCore
    {
        private readonly CloudStorageAccount _account;
        private readonly string _tableName;

        private CloudTableClient _client;

        public LiveTableCore(CloudStorageAccount account, string tableName)
        {
            _account = account;
            _tableName = tableName;

            _client = _account.CreateCloudTableClient();
            _client.CreateTableIfNotExist(tableName);
        }

        public override ITableCorePartitionWriter NewPartitionWriter(string partitionKey)
        {
            return new AzureTableWriteCorePartition(this, partitionKey);
        }

        class AzureTableWriteCorePartition : ITableCorePartitionWriter
        {
            private readonly LiveTableCore _outer;
            private TableServiceContext _ctx = null;
            private readonly string _partitionKey;

            public AzureTableWriteCorePartition(LiveTableCore outer, string partitionKey)
            {
                _outer = outer;
                _partitionKey = partitionKey;
                _ctx = _outer._client.GetDataServiceContext();
                _ctx.WritingEntity += new EventHandler<ReadingWritingEntityEventArgs>(ctx_WritingEntity);
            }

            private void ctx_WritingEntity(object sender, ReadingWritingEntityEventArgs args)
            {
                GenericEntity entity = args.Entity as GenericEntity;
                if (entity == null)
                {
                    return;
                }

                XElement properties = args.Data.Descendants(AzureTableConstants.MetadataNamespace + "properties").First();

                foreach (var kv in entity.properties)
                {
                    // framework will handle row + partition keys. 
                    string columnName = kv.Key;
                    string edmTypeName = "Edm.String";
                    string value = kv.Value;
                    XElement e = new XElement(AzureTableConstants.DataNamespace + columnName, value);
                    e.Add(new XAttribute(AzureTableConstants.MetadataNamespace + "type", edmTypeName));

                    properties.Add(e);
                }
            }

            void ITableCorePartitionWriter.Flush()
            {
                var ctx = _ctx;
                _ctx = null;

                var opts = SaveChangesOptions.Batch | SaveChangesOptions.ReplaceOnUpdate;
                var result = ctx.SaveChangesWithRetries(opts);        
            }

            void ITableCorePartitionWriter.AddObject(GenericEntity entity)
            {
                if (entity.PartitionKey != _partitionKey)
                {
                    // Caller should ensure this doesn't happen. 
                    throw new InvalidOperationException("Partition key mismatch");
                }

                _ctx.AttachTo(_outer._tableName, entity);
                _ctx.UpdateObject(entity);
            }
        }

        public override void DeleteTable()
        {
            // This could take minutes.
            Utility.DeleteTable(_account, _tableName);
        }

        public override void DeleteTableAsync()
        {
            // This could take a while for delete to be finished            
            CloudTableClient tableClient = _account.CreateCloudTableClient();
            tableClient.DeleteTableIfExist(_tableName);
        }

        public override void DeleteTablePartition(string partitionKey)
        {
            Utility.DeleteTablePartition(_account, _tableName, partitionKey);
        }

        public override void DeleteTableRow(string partitionKey, string rowKey)
        {
            Utility.DeleteTableRow(_account, _tableName, partitionKey, rowKey);
        }

        public override IEnumerable<GenericEntity> Enumerate(string partitionKey)
        {
            
            // Azure will special case this lookup pattern for a single entity. 
            // See http://blogs.msdn.com/b/windowsazurestorage/archive/2010/11/06/how-to-get-most-out-of-windows-azure-tables.aspx 
            try
            {
                TableServiceContext ctx = _client.GetDataServiceContext();
                ctx.IgnoreMissingProperties = true;
                ctx.ReadingEntity += OnReadingEntity;

                IQueryable<GenericEntity> query1;
                if (partitionKey == null)
                {
                    query1 = from o in ctx.CreateQuery<GenericEntity>(_tableName)
                             select o;
                }
                else
                {
                    query1 = from o in ctx.CreateQuery<GenericEntity>(_tableName)
                             where o.PartitionKey == partitionKey
                             select o;
                }

                // Careful, must call AsTableServiceQuery() to get more than 1000 rows. 
                // http://blogs.msdn.com/b/rihamselim/archive/2011/01/06/retrieving-more-the-1000-row-from-windows-azure-storage.aspx
                // Query will create an IQueryable and try to retrieve all rows at once. 
                CloudTableQuery<GenericEntity> query2 = query1.AsTableServiceQuery();

                // But then must call Execute to get an deferred execution. 
                // http://convective.wordpress.com/2010/02/06/queries-in-azure-tables/
                IEnumerable<GenericEntity> results = query2.Execute(); // maintain deferred

                return results;
            }
            catch (DataServiceQueryException)
            {
                // Not found. 
                return null;
            }
        }

        [DebuggerNonUserCode]
        public override GenericEntity Lookup(string partitionKey, string rowKey)
        {            
            // Azure will special case this lookup pattern for a single entity. 
            // See http://blogs.msdn.com/b/windowsazurestorage/archive/2010/11/06/how-to-get-most-out-of-windows-azure-tables.aspx 
            try
            {
                TableServiceContext ctx = _client.GetDataServiceContext();
                ctx.IgnoreMissingProperties = true;
                ctx.ReadingEntity += OnReadingEntity;

                var x = from o in ctx.CreateQuery<GenericEntity>(_tableName)
                        where o.PartitionKey == partitionKey && o.RowKey == rowKey
                        select o;
                GenericEntity all = x.First();
                return all;
            }
            catch (DataServiceQueryException)
            {
                // Not found. 
                return null;
            }
        }

        // This manually parses the XML that comes back.
        // This function uses code from this blog entry:
        // http://blogs.msdn.com/b/avkashchauhan/archive/2011/03/28/reading-and-saving-table-storage-entities-without-knowing-the-schema-or-updating-tablestorageentity-schema-at-runtime.aspx
        public static void OnReadingEntity(object sender, ReadingWritingEntityEventArgs args)
        {
            GenericEntity entity = args.Entity as GenericEntity;
            if (entity == null)
            {
                return;
            }

            // read each property, type and value in the payload   
            var properties = args.Entity.GetType().GetProperties();
            var q = from p in args.Data.Element(AzureTableConstants.AtomNamespace + "content")
                                    .Element(AzureTableConstants.MetadataNamespace + "properties")
                                    .Elements()
                    where properties.All(pp => pp.Name != p.Name.LocalName)
                    select new
                    {
                        Name = p.Name.LocalName,
                        IsNull = string.Equals("true", p.Attribute(AzureTableConstants.MetadataNamespace + "null") == null ? null : p.Attribute(AzureTableConstants.MetadataNamespace + "null").Value, StringComparison.OrdinalIgnoreCase),
                        TypeName = p.Attribute(AzureTableConstants.MetadataNamespace + "type") == null ? null : p.Attribute(AzureTableConstants.MetadataNamespace + "type").Value,
                        p.Value
                    };

            foreach (var dp in q)
            {
                // $$$ Could do type marshaling here. 
                string value = dp.Value;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    value = string.Empty;
                }
                entity.properties[dp.Name] = dp.Value;
            }
        }
    }
}