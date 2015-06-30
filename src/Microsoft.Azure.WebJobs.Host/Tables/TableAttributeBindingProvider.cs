// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Table;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    internal class TableAttributeBindingProvider : IBindingProvider
    {
        private readonly IStorageTableArgumentBindingProvider _tableBindingProvider;
        private readonly ITableEntityArgumentBindingProvider _entityBindingProvider;

        private readonly INameResolver _nameResolver;
        private readonly IStorageAccountProvider _accountProvider;

        public TableAttributeBindingProvider(INameResolver nameResolver, IStorageAccountProvider accountProvider, IExtensionRegistry extensions)
        {
            if (accountProvider == null)
            {
                throw new ArgumentNullException("accountProvider");
            }

            if (extensions == null)
            {
                throw new ArgumentNullException("extensions");
            }

            _nameResolver = nameResolver;
            _accountProvider = accountProvider;

            _tableBindingProvider = new CompositeArgumentBindingProvider(
                new StorageTableArgumentBindingProvider(),
                new CloudTableArgumentBindingProvider(),
                new QueryableArgumentBindingProvider(),
                new CollectorArgumentBindingProvider(),
                new AsyncCollectorArgumentBindingProvider(),
                new TableArgumentBindingExtensionProvider(extensions));

            _entityBindingProvider =
                new CompositeEntityArgumentBindingProvider(
                new TableEntityArgumentBindingProvider(),
                new PocoEntityArgumentBindingProvider()); // Supports all types; must come after other providers
        }

        public async Task<IBinding> TryCreateAsync(BindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;
            TableAttribute tableAttribute = parameter.GetCustomAttribute<TableAttribute>(inherit: false);

            if (tableAttribute == null)
            {
                return null;
            }

            string tableName = Resolve(tableAttribute.TableName);
            IStorageAccount account = await _accountProvider.GetStorageAccountAsync(context.CancellationToken);
            IStorageTableClient client = account.CreateTableClient();

            bool bindsToEntireTable = tableAttribute.RowKey == null;
            IBinding binding;

            if (bindsToEntireTable)
            {
                IBindableTablePath path = BindableTablePath.Create(tableName);
                path.ValidateContractCompatibility(context.BindingDataContract);

                IStorageTableArgumentBinding argumentBinding = _tableBindingProvider.TryCreate(parameter);

                if (argumentBinding == null)
                {
                    throw new InvalidOperationException("Can't bind Table to type '" + parameter.ParameterType + "'.");
                }

                binding = new TableBinding(parameter.Name, argumentBinding, client, path);
            }
            else
            {
                string partitionKey = Resolve(tableAttribute.PartitionKey);
                string rowKey = Resolve(tableAttribute.RowKey);
                IBindableTableEntityPath path = BindableTableEntityPath.Create(tableName, partitionKey, rowKey);
                path.ValidateContractCompatibility(context.BindingDataContract);

                IArgumentBinding<TableEntityContext> argumentBinding = _entityBindingProvider.TryCreate(parameter);

                if (argumentBinding == null)
                {
                    throw new InvalidOperationException("Can't bind Table entity to type '" + parameter.ParameterType + "'.");
                }

                binding = new TableEntityBinding(parameter.Name, argumentBinding, client, path);
            }

            return binding;
        }

        private string Resolve(string queueName)
        {
            if (_nameResolver == null)
            {
                return queueName;
            }

            return _nameResolver.ResolveWholeString(queueName);
        }
    }
}
