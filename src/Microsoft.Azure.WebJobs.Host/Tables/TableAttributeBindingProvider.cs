// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    internal class TableAttributeBindingProvider : IBindingProvider
    {
        private static readonly ITableArgumentBindingProvider _tableProvider = new CompositeArgumentBindingProvider(
            new CloudTableArgumentBindingProvider(),
            new QueryableArgumentBindingProvider(),
            new CollectorArgumentBindingProvider(),
            new AsyncCollectorArgumentBindingProvider());

        private static readonly ITableEntityArgumentBindingProvider _entityProvider =
            new CompositeEntityArgumentBindingProvider(
                new TableEntityArgumentBindingProvider(),
                new PocoEntityArgumentBindingProvider()); // Supports all types; must come after other providers

        public Task<IBinding> TryCreateAsync(BindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;
            TableAttribute tableAttribute = parameter.GetCustomAttribute<TableAttribute>(inherit: false);

            if (tableAttribute == null)
            {
                return Task.FromResult<IBinding>(null);
            }

            string tableName = context.Resolve(tableAttribute.TableName);
            CloudTableClient client = context.StorageAccount.CreateCloudTableClient();
            Type parameterType = parameter.ParameterType;

            bool bindsToEntireTable = tableAttribute.RowKey == null;
            IBinding binding;

            if (bindsToEntireTable)
            {
                IBindableTablePath path = BindableTablePath.Create(tableName);
                path.ValidateContractCompatibility(context.BindingDataContract);

                ITableArgumentBinding argumentBinding = _tableProvider.TryCreate(parameterType);

                if (argumentBinding == null)
                {
                    throw new InvalidOperationException("Can't bind Table to type '" + parameterType + "'.");
                }

                binding = new TableBinding(parameter.Name, argumentBinding, client, path);
            }
            else
            {
                string partitionKey = context.Resolve(tableAttribute.PartitionKey);
                string rowKey = context.Resolve(tableAttribute.RowKey);
                IBindableTableEntityPath path = BindableTableEntityPath.Create(tableName, partitionKey, rowKey);
                path.ValidateContractCompatibility(context.BindingDataContract);

                IArgumentBinding<TableEntityContext> argumentBinding = _entityProvider.TryCreate(parameterType);

                if (argumentBinding == null)
                {
                    throw new InvalidOperationException("Can't bind Table entity to type '" + parameterType + "'.");
                }

                binding = new TableEntityBinding(parameter.Name, argumentBinding, client, path);
            }

            return Task.FromResult(binding);
        }
    }
}
