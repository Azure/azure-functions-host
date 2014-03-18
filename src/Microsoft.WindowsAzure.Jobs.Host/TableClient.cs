using System;
using System.Collections.Generic;
using System.Data.Services.Client;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;
using AzureTables;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    // Functions for working with azure tables.
    // See http://msdn.microsoft.com/en-us/library/windowsazure/dd179338.aspx
    //
    // Naming rules:
    // RowKey  - no \,/, #, ?, less than 1 kb in size
    // Table name is restrictive, must match: "^[A-Za-z][A-Za-z0-9]{2,62}$"
    internal static class TableClient
    {
        // Convert key into something that can be used as a row or partition key. Removes invalid chars.
        public static string GetAsTableKey(string key)
        {
            return key.Replace('\\', '.').Replace('/', '.');
        }

        // Helper to get a row key based on time stamp. 
        // Where recent time is sorted first. 
        public static string GetTickRowKey(DateTime time, Guid id)
        {
            string rowKey = Ticks(time) + "." + id.ToString("N");
            return rowKey;
        }

        public static string GetTickRowKey()
        {
            string rowKey = Ticks(DateTime.UtcNow);
            return rowKey;
        }

        private static string Ticks(DateTime time)
        {
            return string.Format("{0:D19}", DateTime.MaxValue.Ticks - time.Ticks);
        }

        // Is this a type that is already serialized by default?
        // See list of types here: http://msdn.microsoft.com/en-us/library/windowsazure/dd179338.aspx
        public static bool IsDefaultTableType(Type t)
        {
            if ((t == typeof(byte[])) ||
                (t == typeof(bool)) ||
                (t == typeof(DateTime)) ||
                (t == typeof(double)) ||
                (t == typeof(Guid)) ||
                (t == typeof(Int32)) ||
                (t == typeof(Int64)) ||
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
                    return IsDefaultTableType(tArg);
                }
            }

            return false;
        }

        public static void DeleteTableRow(CloudStorageAccount account, string tableName, string partitionKey, string rowKey)
        {
            // http://www.windowsazure.com/en-us/develop/net/how-to-guides/table-services/#delete-entity

            // Retrieve storage account from connection-string

            // Create the table client
            CloudTableClient tableClient = account.CreateCloudTableClient();

            // Get the data service context
            TableServiceContext serviceContext = tableClient.GetDataServiceContext();

            PartitionRowKeyEntity specificEntity = null;

            try
            {
                specificEntity =
                    (from e in serviceContext.CreateQuery<PartitionRowKeyEntity>(tableName)
                     where e.PartitionKey == partitionKey && e.RowKey == rowKey
                     select e).FirstOrDefault();
            }
            catch (DataServiceQueryException)
            {
                // Throws if entry doesn't exist. 
            }

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

        // Azure table names are very restrictive, so sanity check upfront to give a useful error.
        // http://msdn.microsoft.com/en-us/library/windowsazure/dd179338.aspx
        public static void ValidateAzureTableName(string tableName)
        {
            if (!Regex.IsMatch(tableName, "^[A-Za-z][A-Za-z0-9]{2,62}$"))
            {
                throw new InvalidOperationException(string.Format("'{0}' is not a valid name for an azure table", tableName));
            }
        }
    }
}
