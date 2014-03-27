using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class CloudTableEntityDescriptor
    {
        public string AccountConnectionString { get; set; }

        public string TableName { get; set; }

        public string PartitionKey { get; set; }

        public string RowKey { get; set; }

        public void Validate()
        {
            TableClient.ValidateAzureTableName(TableName);
            TableClient.ValidateAzureTableKeyValue(PartitionKey);
            TableClient.ValidateAzureTableKeyValue(RowKey);
        }

        // Return new entity descriptor with names filled in. 
        // Throws if any unbound values. 
        public static CloudTableEntityDescriptor ApplyNames(string tableName, string partitionKey, string rowKey, IDictionary<string, string> nameParameters)
        {
            return new CloudTableEntityDescriptor
            {
                TableName = RouteParser.ApplyNames(tableName, nameParameters),
                PartitionKey = RouteParser.ApplyNames(partitionKey, nameParameters),
                RowKey = RouteParser.ApplyNames(rowKey, nameParameters)
            };
        }

        public static CloudTableEntityDescriptor Parse(string value)
        {
            CloudTableEntityDescriptor entityDescriptor;

            if (!TryParse(value, out entityDescriptor))
            {
                throw new InvalidOperationException("Table entity identifiers must be in the format TableName/PartitionKey/RowKey.");
            }

            return entityDescriptor;
        }

        public static bool TryParse(string value, out CloudTableEntityDescriptor entityDescriptor)
        {
            if (value == null)
            {
                entityDescriptor = null;
                return false;
            }

            string[] components = value.Split(new char[] { '/' });
            if (components.Length != 3)
            {
                entityDescriptor = null;
                return false;
            }

            entityDescriptor = new CloudTableEntityDescriptor
            {
                TableName = components[0],
                PartitionKey = components[1],
                RowKey = components[2]
            };
            return true;
        }
    }
}
