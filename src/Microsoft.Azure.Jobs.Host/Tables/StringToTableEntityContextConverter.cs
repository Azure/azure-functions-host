using System;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Tables
{
    internal class StringToTableEntityContextConverter : IConverter<string, TableEntityContext>
    {
        private readonly CloudTableClient _client;
        private readonly string _defaultTableName;
        private readonly string _defaultPartitionKey;
        private readonly string _defaultRowKey;

        public StringToTableEntityContextConverter(CloudTableClient client, string defaultTableName, string defaultPartitionKey,
            string defaultRowKey)
        {
            _client = client;
            _defaultTableName = defaultTableName;
            _defaultPartitionKey = defaultPartitionKey;
            _defaultRowKey = defaultRowKey;
        }

        public TableEntityContext Convert(string input)
        {
            // For convenience, treat an an empty string as a request for the default value (when valid).
            if (String.IsNullOrEmpty(input) && !RouteParser.HasParameterNames(_defaultTableName)
                && !RouteParser.HasParameterNames(_defaultPartitionKey)
                && !RouteParser.HasParameterNames(_defaultRowKey))
            {
                return new TableEntityContext
                {
                    Table = _client.GetTableReference(_defaultTableName),
                    PartitionKey = _defaultPartitionKey,
                    RowKey = _defaultRowKey
                };
            }

            CloudTableEntityDescriptor descriptor = CloudTableEntityDescriptor.Parse(input);
            TableClient.ValidateAzureTableName(descriptor.TableName);

            return new TableEntityContext
            {
                Table = _client.GetTableReference(descriptor.TableName),
                PartitionKey = descriptor.PartitionKey,
                RowKey = descriptor.RowKey
            };
        }
    }
}
