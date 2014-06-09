using System;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Tables
{
    internal class TableAttributeBindingProvider : IBindingProvider
    {
        private static readonly ITableArgumentBindingProvider _tableProvider = new CompositeArgumentBindingProvider(
            new CloudTableArgumentBindingProvider(),
            new QueryableArgumentBindingProvider());

        private static readonly ITableEntityArgumentBindingProvider _entityProvider =
            new CompositeEntityArgumentBindingProvider(
                new TableEntityArgumentBindingProvider(),
                new PocoEntityArgumentBindingProvider()); // Supports all types; must come after other providers

        public IBinding TryCreate(BindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;
            TableAttribute tableAttribute = parameter.GetCustomAttribute<TableAttribute>(inherit: false);

            if (tableAttribute == null)
            {
                return null;
            }

            string tableName = context.Resolve(tableAttribute.TableName);
            if (!RouteParser.HasParameterNames(tableName))
            {
                TableClient.ValidateAzureTableName(tableName);
            }

            CloudTableClient client = context.StorageAccount.CreateCloudTableClient();

            Type parameterType = parameter.ParameterType;
            bool bindsToEntireTable = tableAttribute.RowKey == null;

            if (bindsToEntireTable)
            {
                IArgumentBinding<CloudTable> argumentBinding = _tableProvider.TryCreate(parameterType);

                if (argumentBinding == null)
                {
                    throw new InvalidOperationException("Can't bind Table to type '" + parameterType + "'.");
                }

                return new TableBinding(parameter.Name, argumentBinding, client, tableName);
            }
            else
            {
                string partitionKey = context.Resolve(tableAttribute.PartitionKey);

                if (!RouteParser.HasParameterNames(partitionKey))
                {
                    TableClient.ValidateAzureTableKeyValue(partitionKey);
                }

                string rowKey = context.Resolve(tableAttribute.RowKey);

                if (!RouteParser.HasParameterNames(rowKey))
                {
                    TableClient.ValidateAzureTableKeyValue(rowKey);
                }

                IArgumentBinding<TableEntityContext> argumentBinding = _entityProvider.TryCreate(parameterType);

                if (argumentBinding == null)
                {
                    throw new InvalidOperationException("Can't bind Table entity to type '" + parameterType + "'.");
                }

                return new TableEntityBinding(parameter.Name, argumentBinding, client, tableName, partitionKey, rowKey);
            }
        }
    }
}
