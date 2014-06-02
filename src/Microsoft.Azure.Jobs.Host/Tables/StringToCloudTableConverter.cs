using System;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Tables
{
    internal class StringToCloudTableConverter : IConverter<string, CloudTable>
    {
        private readonly CloudTableClient _client;
        private readonly string _defaultTableName;

        public StringToCloudTableConverter(CloudTableClient client, string defaultTableName)
        {
            _client = client;
            _defaultTableName = defaultTableName;
        }

        public CloudTable Convert(string input)
        {
            // For convenience, treat an an empty string as a request for the default value (when valid).
            if (String.IsNullOrEmpty(input) && !RouteParser.HasParameterNames(_defaultTableName))
            {
                return _client.GetTableReference(_defaultTableName);
            }

            TableClient.ValidateAzureTableName(input);
            return _client.GetTableReference(input);
        }
    }
}
