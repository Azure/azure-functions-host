using System;

namespace Microsoft.Azure.Jobs
{
    internal class CloudTableEntityDescriptor
    {
        public string TableName { get; set; }

        public string PartitionKey { get; set; }

        public string RowKey { get; set; }

        public void Validate()
        {
            TableClient.ValidateAzureTableName(TableName);
            TableClient.ValidateAzureTableKeyValue(PartitionKey);
            TableClient.ValidateAzureTableKeyValue(RowKey);
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
