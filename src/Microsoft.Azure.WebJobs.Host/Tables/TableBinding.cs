// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Storage.Table;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    internal class TableBinding : IBinding
    {
        private readonly string _parameterName;
        private readonly ITableArgumentBinding _argumentBinding;
        private readonly IStorageTableClient _client;
        private readonly string _accountName;
        private readonly IBindableTablePath _path;
        private readonly IObjectToTypeConverter<IStorageTable> _converter;

        public TableBinding(string parameterName, ITableArgumentBinding argumentBinding, IStorageTableClient client,
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
                return _argumentBinding.Access;
            }
        }

        private static IObjectToTypeConverter<IStorageTable> CreateConverter(IStorageTableClient client,
            IBindableTablePath path)
        {
            return new CompositeObjectToTypeConverter<IStorageTable>(
                new OutputConverter<IStorageTable>(new IdentityConverter<IStorageTable>()),
                new OutputConverter<CloudTable>(new CloudTableToStorageTableConverter()),
                new OutputConverter<string>(new StringToStorageTableConverter(client, path)));
        }

        public Task<IValueProvider> BindAsync(BindingContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            string boundTableName = _path.Bind(context.BindingData);
            IStorageTable table = _client.GetTableReference(boundTableName);

            return BindTableAsync(table, context.ValueContext);
        }

        private Task<IValueProvider> BindTableAsync(IStorageTable value, ValueBindingContext context)
        {
            return _argumentBinding.BindAsync(value, context);
        }

        public Task<IValueProvider> BindAsync(object value, ValueBindingContext context)
        {
            IStorageTable table = null;

            if (!_converter.TryConvert(value, out table))
            {
                throw new InvalidOperationException("Unable to convert value to IStorageTable.");
            }

            return BindTableAsync(table, context);
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
