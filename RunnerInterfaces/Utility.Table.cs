using System;
using System.Collections.Generic;
using System.Data.Services.Client;
using System.Data.Services.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using AzureTables;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;

namespace RunnerInterfaces
{
    // Functions for working with azure tables.
    // See http://msdn.microsoft.com/en-us/library/windowsazure/dd179338.aspx
    //
    // Naming rules:
    // RowKey  - no \,/, #, ?, less than 1 kb in size
    // Table name is restrictive, must match: "^[A-Za-z][A-Za-z0-9]{2,62}$"
    public static partial class Utility
    {
        private static TableServiceContext GetContent<T>(CloudStorageAccount account, string tableName)
        {
            CloudTableClient tableClient = account.CreateCloudTableClient();
            return GetContent<T>(tableClient, tableName);
        }

        private static TableServiceContext GetContent<T>(CloudTableClient tableClient, string tableName)
        {
            tableClient.CreateTableIfNotExist(tableName);
            TableServiceContext serviceContext = tableClient.GetDataServiceContext();
            serviceContext.WritingEntity += serviceContext_WritingEntity<T>;
            serviceContext.ReadingEntity += serviceContext_ReadingEntity<T>;

            return serviceContext;
        }
                
        public static void AddTableRow<T>(CloudStorageAccount account, string tableName, T row) where T : TableServiceEntity
        {
            TableServiceContext serviceContext = GetContent<T>(account, tableName);

            // Do an Upsert (insert or replace)
            // http://blogs.msdn.com/b/windowsazurestorage/archive/2011/09/15/windows-azure-tables-introducing-upsert-and-query-projection.aspx

            //serviceContext.AddObject(tableName, row);
            serviceContext.AttachTo(tableName, row);
            serviceContext.UpdateObject(row);

            // If this fails with 400, when running against the emulator, it could be because the emulator 
            // doesn't support an upsert. 
            // If T has an enum property in it, the azure sdk will hang and not even invoke the WritingEntity 
            serviceContext.SaveChangesWithRetries(SaveChangesOptions.ReplaceOnUpdate);
        }

        // Is this a type that is already serialized by default?
        // See list of types here: http://msdn.microsoft.com/en-us/library/windowsazure/dd179338.aspx
        public static bool IsSimpleType(Type t)
        {
            if ((t == typeof(byte[])) ||
                (t == typeof(bool)) ||
                (t == typeof(DateTime)) ||
                (t == typeof(double)) ||
                (t == typeof(Guid)) ||                
                (t == typeof(System.Int32)) ||
                (t == typeof(System.Int64)) || 
                (t == typeof(string))
                ) 
            {
                return true;
            }

            // Nullables are written too. 
            if (t.IsGenericType)
            {
                var tOpen = t.GetGenericTypeDefinition();
                if (tOpen == typeof(Nullable<>))
                {
                    var tArg = t.GetGenericArguments()[0];
                    return IsSimpleType(tArg);
                }
            }
            
            return false;
        }

        static void serviceContext_ReadingEntity<T>(object sender, ReadingWritingEntityEventArgs args)
        {
            T entity = (T)args.Entity;
            if (entity == null)
            {
                return;
            }


            var xmlProps = args.Data.Element(AzureTable.AtomNamespace + "content")
                            .Element(AzureTable.MetadataNamespace + "properties")
                            .Elements();
            foreach (var xmlProp in xmlProps)
            {
                string name = xmlProp.Name.LocalName;
                PropertyInfo prop = typeof(T).GetProperty(name);
                if (prop == null)
                {
                    continue; // extra entity property not in C# class. Ignore it.
                }

                bool isNull = string.Equals("true", xmlProp.Attribute(AzureTable.MetadataNamespace + "null") == null ? null : xmlProp.Attribute(AzureTable.MetadataNamespace + "null").Value, StringComparison.OrdinalIgnoreCase);

                if (isNull)
                {
                    continue; // nothing to do with a null value. 
                }

                string typeName = xmlProp.Attribute(AzureTable.MetadataNamespace + "type") == null ? null : xmlProp.Attribute(AzureTable.MetadataNamespace + "type").Value;

                string inputValue = xmlProp.Value;

                // simple types are already prepopulated
                if (IsSimpleType(prop.PropertyType))
                {
                    continue;
                }

                // Assume complex object, use JSON
                object value = JsonCustom.DeserializeObject(inputValue, prop.PropertyType);
                prop.SetValue(entity, value, null);
            }
        }

        // See list of types here: http://msdn.microsoft.com/en-us/library/windowsazure/dd179338.aspx
        static void serviceContext_WritingEntity<T>(object sender, ReadingWritingEntityEventArgs args)
        {
            T obj = (T) args.Entity;

            XElement properties = args.Data.Descendants(AzureTable.MetadataNamespace + "properties").First();

            
            foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var value = property.GetValue(obj, null);

                var propType = property.PropertyType;

                if (IsSimpleType(propType))
                {
                    continue;
                }

                // Custom objects, serialize as Json, store as a string.
                // Reading the table entity can deserialize these.
                string edmTypeName = "Edm.String";
                string content = JsonCustom.SerializeObject(value);
                
                // Remove property with "Name"
                RemoveXmlProperty(property.Name, properties);

                // Add new version.
                AddXmlProperty(property.Name, content, edmTypeName, properties);                
            }
        }

        private static void AddXmlProperty(string propertyName, string content, string edmTypeName, XElement properties)
        {
            XElement e = new XElement(AzureTable.DataNamespace + propertyName, content);
            e.Add(new XAttribute(AzureTable.MetadataNamespace + "type", edmTypeName));

            properties.Add(e);
        }

        private static void RemoveXmlProperty(string propertyName, XElement xePayload)
        {
            // XName xnEntityProperties = XName.Get("properties", AzureTable.MetadataNamespace.NamespaceName);
            // var xePayload = args.Data.Descendants().Where<XElement>(xe => xe.Name == xnEntityProperties).First<XElement>();

            //XName xnProperty = XName.Get(propertyName, args.Data.GetNamespaceOfPrefix("d").NamespaceName);
            XName xnProperty = XName.Get(propertyName, AzureTable.DataNamespace.NamespaceName);
            foreach (XElement xeRemoveThisProperty in xePayload.Descendants(xnProperty).ToList())
            {
                xeRemoveThisProperty.Remove();
            }
        }

        public static void DeleteTableRow<T>(CloudStorageAccount account, string tableName, T row) where T : TableServiceEntity
        {
            // http://www.windowsazure.com/en-us/develop/net/how-to-guides/table-services/#delete-entity

            // Retrieve storage account from connection-string
            
            // Create the table client
            CloudTableClient tableClient = account.CreateCloudTableClient();

            // Get the data service context
            TableServiceContext serviceContext = tableClient.GetDataServiceContext();

            T specificEntity =
                (from e in serviceContext.CreateQuery<T>(tableName)
                 where e.PartitionKey == row.PartitionKey && e.RowKey == row.RowKey
                 select e).FirstOrDefault();

            if (specificEntity != null)
            {
                // Delete the entity
                serviceContext.DeleteObject(specificEntity);

                // Submit the operation to the table service
                serviceContext.SaveChangesWithRetries();
            }
        }

        public static void DeleteTableRow(CloudStorageAccount account, string tableName, string partitionKey, string rowKey) 
        {
            // http://www.windowsazure.com/en-us/develop/net/how-to-guides/table-services/#delete-entity

            // Retrieve storage account from connection-string

            // Create the table client
            CloudTableClient tableClient = account.CreateCloudTableClient();

            // Get the data service context
            TableServiceContext serviceContext = tableClient.GetDataServiceContext();

            PartitionRowKeyEntity specificEntity =
                (from e in serviceContext.CreateQuery<PartitionRowKeyEntity>(tableName)
                 where e.PartitionKey == partitionKey && e.RowKey == rowKey
                 select e).FirstOrDefault();

            if (specificEntity != null)
            {
                // Delete the entity
                serviceContext.DeleteObject(specificEntity);

                // Submit the operation to the table service
                serviceContext.SaveChangesWithRetries();
            }
        }

        // Delete an entire partition
        public static void DeleteTablePartition(CloudStorageAccount account, string tableName, string partitionKey)
        {
            // No shortcut for deleting a partition
            // http://stackoverflow.com/questions/7393651/can-i-delete-an-entire-partition-in-windows-azure-table-storage

            // http://www.windowsazure.com/en-us/develop/net/how-to-guides/table-services/#delete-entity

            CloudTableClient tableClient = account.CreateCloudTableClient();
            TableServiceContext serviceContext = tableClient.GetDataServiceContext();

            // Loop and delete in batches
            while (true)
            {
                IQueryable<PartitionRowKeyEntity> list;

                try
                {
                    list = from e in serviceContext.CreateQuery<PartitionRowKeyEntity>(tableName)
                           where e.PartitionKey == partitionKey
                           select e;
                }
                catch
                {
                    // Azure sometimes throws an exception when enumerating an empty table.
                    return;
                }

                int count = 0;
                
                foreach (var item in list)
                {
                    count++;
                    // Delete the entity
                    serviceContext.DeleteObject(item);
                }
                if (count == 0)
                {
                    return;
                }

                // Submit the operation to the table service
                serviceContext.SaveChangesWithRetries();
            }
        }

        // Beware! Delete could take a very long time. 
        [DebuggerNonUserCode] // Hide first chance exceptions from delete polling.
        public static void DeleteTable(CloudStorageAccount account, string tableName)
        {
            CloudTableClient tableClient = account.CreateCloudTableClient();
            tableClient.DeleteTableIfExist(tableName);

            // Delete returns synchronously even though table is not yet deleted. Losers!!
            // So poll here until we're in a known good state.
            while (true)
            {
                try
                {
                    tableClient.CreateTableIfNotExist(tableName);
                    break;
                }
                catch (StorageClientException)
                {
                    Thread.Sleep(1 * 1000);
                }
            }
        }

        // Table will come back sorted by (partition, row key)
        // Integers don't sort nicely as strings.
        [DebuggerNonUserCode]
        public static T[] ReadTable<T>(CloudStorageAccount account, string tableName) where T : TableServiceEntity
        {
            TableServiceContext ctx = GetContent<T>(account, tableName);

            var query = from row in ctx.CreateQuery<T>(tableName) select row;
            var query2 = query.AsTableServiceQuery<T>();

            // Verify table matches source
            try
            {
                T[] result = query2.ToArray();
                return result;
            }
            catch(DataServiceQueryException)
            {                
                // Table does not exist
                return new T[0];
            }            
        }

        public static IEnumerable<T> ReadTableLazy<T>(CloudStorageAccount account, string tableName) where T : TableServiceEntity
        {
            TableServiceContext ctx = GetContent<T>(account, tableName);

            var query = from row in ctx.CreateQuery<T>(tableName) select row;
            var query2 = query.AsTableServiceQuery<T>();

            return query2;
        }   

        public static T Lookup<T>(CloudStorageAccount account, string tableName, string partitionKey, string rowKey) where T : TableServiceEntity
        {
            CloudTableClient tableClient = account.CreateCloudTableClient();
            return Lookup<T>(tableClient, tableName, partitionKey, rowKey);
        }

        // Lookup a single entity. 
        // Return null if not found. 
        [DebuggerNonUserCode]
        public static T Lookup<T>(CloudTableClient tableClient, string tableName, string partitionKey, string rowKey) where T : TableServiceEntity
        {
            TableServiceContext ctx = GetContent<T>(tableClient, tableName);

            // Azure will special case this lookup pattern for a single entity. 
            // See http://blogs.msdn.com/b/windowsazurestorage/archive/2010/11/06/how-to-get-most-out-of-windows-azure-tables.aspx 
            try
            {
                // This will throw DataServiceQueryException if not found. (as opposed to return an empty query)
                var x = from row in ctx.CreateQuery<T>(tableName)
                        where row.PartitionKey == partitionKey && row.RowKey == rowKey
                        select row;
                var x2 = x.AsTableServiceQuery<T>();

                return x2.First();
            }
            catch (DataServiceQueryException)
            {
                // Not found. 
                return null;
            }
        }
    }

    // Delete still needs an entity, but just the partition and row keys. 
    [DataServiceKey("PartitionKey", "RowKey")]
    internal class PartitionRowKeyEntity
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
    }
}
