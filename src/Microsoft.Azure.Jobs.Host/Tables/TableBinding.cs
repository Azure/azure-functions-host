// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Tables
{
    internal class TableBinding : IBinding
    {
        private readonly string _parameterName;
        private readonly IArgumentBinding<CloudTable> _argumentBinding;
        private readonly CloudTableClient _client;
        private readonly string _accountName;
        private readonly IBindableTablePath _path;
        private readonly IObjectToTypeConverter<CloudTable> _converter;

        public TableBinding(string parameterName, IArgumentBinding<CloudTable> argumentBinding, CloudTableClient client,
            IBindableTablePath path)
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

        private FileAccess Access
        {
            get
            {
                return _argumentBinding.ValueType == typeof(CloudTable)
                    ? FileAccess.ReadWrite : FileAccess.Read;
            }
        }

        private static IObjectToTypeConverter<CloudTable> CreateConverter(CloudTableClient client,
            IBindableTablePath path)
        {
            return new CompositeObjectToTypeConverter<CloudTable>(
                new OutputConverter<CloudTable>(new IdentityConverter<CloudTable>()),
                new OutputConverter<string>(new StringToCloudTableConverter(client, path)));
        }

        public IValueProvider Bind(BindingContext context)
        {
            string boundTableName = _path.Bind(context.BindingData);
            CloudTable table = _client.GetTableReference(boundTableName);

            return Bind(table, context.FunctionContext);
        }

        private IValueProvider Bind(CloudTable value, FunctionBindingContext context)
        {
            return _argumentBinding.Bind(value, context);
        }

        public IValueProvider Bind(object value, FunctionBindingContext context)
        {
            CloudTable table = null;

            if (!_converter.TryConvert(value, out table))
            {
                throw new InvalidOperationException("Unable to convert value to CloudTable.");
            }

            return Bind(table, context);
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            return new TableParameterDescriptor
            {
                Name = _parameterName,
                AccountName = _accountName,
                TableName = _path.TableNamePattern,
                Access = Access
            };
        }
    }
}
