using System;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Tables
{
    internal class TableEntityBinding : IBinding
    {
        private readonly string _parameterName;
        private readonly IArgumentBinding<TableEntityContext> _argumentBinding;
        private readonly CloudTableClient _client;
        private readonly string _accountName;
        private readonly string _tableName;
        private readonly string _partitionKey;
        private readonly string _rowKey;
        private readonly IObjectToTypeConverter<TableEntityContext> _converter;

        public TableEntityBinding(string parameterName, IArgumentBinding<TableEntityContext> argumentBinding,
            CloudTableClient client, string tableName, string partitionKey, string rowKey)
        {
            _parameterName = parameterName;
            _argumentBinding = argumentBinding;
            _client = client;
            _accountName = TableClient.GetAccountName(client);
            _tableName = tableName;
            _partitionKey = partitionKey;
            _rowKey = rowKey;
            _converter = CreateConverter(client, tableName, partitionKey, rowKey);
        }

        public bool FromAttribute
        {
            get { return true; }
        }

        public string TableName
        {
            get { return _tableName; }
        }

        public string PartitionKey
        {
            get { return _partitionKey; }
        }

        public string RowKey
        {
            get { return _rowKey; }
        }

        private static IObjectToTypeConverter<TableEntityContext> CreateConverter(CloudTableClient client,
            string tableName, string partitionKey, string rowKey)
        {
            return new CompositeObjectToTypeConverter<TableEntityContext>(
                new EntityOutputConverter<TableEntityContext>(new IdentityConverter<TableEntityContext>()),
                new EntityOutputConverter<string>(new StringToTableEntityContextConverter(client, tableName,
                    partitionKey, rowKey)));
        }

        public IValueProvider Bind(BindingContext context)
        {
            string resolvedTableName = RouteParser.ApplyBindingData(_tableName, context.BindingData);
            TableClient.ValidateAzureTableName(resolvedTableName);
            CloudTable table = _client.GetTableReference(resolvedTableName);

            string resolvedPartitionKey = RouteParser.ApplyBindingData(_partitionKey, context.BindingData);
            TableClient.ValidateAzureTableKeyValue(resolvedPartitionKey);

            string resolvedRowKey = RouteParser.ApplyBindingData(_rowKey, context.BindingData);
            TableClient.ValidateAzureTableKeyValue(resolvedRowKey);

            TableEntityContext entityContext = new TableEntityContext
            {
                Table = table,
                PartitionKey = resolvedPartitionKey,
                RowKey = resolvedRowKey
            };

            return Bind(entityContext, context);
        }

        private IValueProvider Bind(TableEntityContext entityContext, ArgumentBindingContext context)
        {
            return _argumentBinding.Bind(entityContext, context);
        }

        public IValueProvider Bind(object value, ArgumentBindingContext context)
        {
            TableEntityContext entityContext = null;

            if (!_converter.TryConvert(value, out entityContext))
            {
                throw new InvalidOperationException("Unable to convert value to TableEntityContext.");
            }

            TableClient.ValidateAzureTableKeyValue(entityContext.PartitionKey);
            TableClient.ValidateAzureTableKeyValue(entityContext.RowKey);

            return Bind(entityContext, context);
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            return new TableEntityParameterDescriptor
            {
                Name = _parameterName,
                AccountName = _accountName,
                TableName = _tableName,
                PartitionKey = _partitionKey,
                RowKey = _rowKey
            };
        }
    }
}
