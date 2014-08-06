// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
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
        private readonly IBindableTableEntityPath _path;
        private readonly IObjectToTypeConverter<TableEntityContext> _converter;

        public TableEntityBinding(string parameterName, IArgumentBinding<TableEntityContext> argumentBinding,
            CloudTableClient client, IBindableTableEntityPath path)
        {
            _parameterName = parameterName;
            _argumentBinding = argumentBinding;
            _client = client;
            _accountName = TableClient.GetAccountName(client);
            _path = path;
            _converter = CreateConverter(client, path);
        }

        public bool FromAttribute
        {
            get { return true; }
        }

        public string TableName
        {
            get { return _path.TableNamePattern; }
        }

        public string PartitionKey
        {
            get { return _path.PartitionKeyPattern; }
        }

        public string RowKey
        {
            get { return _path.RowKeyPattern; }
        }

        private static IObjectToTypeConverter<TableEntityContext> CreateConverter(CloudTableClient client,
            IBindableTableEntityPath path)
        {
            return new CompositeObjectToTypeConverter<TableEntityContext>(
                new EntityOutputConverter<TableEntityContext>(new IdentityConverter<TableEntityContext>()),
                new EntityOutputConverter<string>(new StringToTableEntityContextConverter(client, path)));
        }

        public Task<IValueProvider> BindAsync(BindingContext context)
        {
            TableEntityPath boundPath = _path.Bind(context.BindingData);
            CloudTable table = _client.GetTableReference(boundPath.TableName);

            TableEntityContext entityContext = new TableEntityContext
            {
                Table = table,
                PartitionKey = boundPath.PartitionKey,
                RowKey = boundPath.RowKey
            };

            return BindAsync(entityContext, context.ValueContext);
        }

        private Task<IValueProvider> BindAsync(TableEntityContext entityContext, ValueBindingContext context)
        {
            return _argumentBinding.BindAsync(entityContext, context);
        }

        public Task<IValueProvider> BindAsync(object value, ValueBindingContext context)
        {
            TableEntityContext entityContext = null;

            if (!_converter.TryConvert(value, out entityContext))
            {
                throw new InvalidOperationException("Unable to convert value to TableEntityContext.");
            }

            TableClient.ValidateAzureTableKeyValue(entityContext.PartitionKey);
            TableClient.ValidateAzureTableKeyValue(entityContext.RowKey);

            return BindAsync(entityContext, context);
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            return new TableEntityParameterDescriptor
            {
                Name = _parameterName,
                AccountName = _accountName,
                TableName = _path.TableNamePattern,
                PartitionKey = _path.PartitionKeyPattern,
                RowKey = _path.RowKeyPattern
            };
        }
    }
}
