// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Table;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    internal class TableAttributeBindingProvider : IBindingProvider
    {
        private readonly ITableEntityArgumentBindingProvider _entityBindingProvider;

        private readonly INameResolver _nameResolver;
        private readonly IStorageAccountProvider _accountProvider;

        public TableAttributeBindingProvider(INameResolver nameResolver, IStorageAccountProvider accountProvider)
        {
            if (accountProvider == null)
            {
                throw new ArgumentNullException("accountProvider");
            }

            _nameResolver = nameResolver;
            _accountProvider = accountProvider;

            _entityBindingProvider =
                new CompositeEntityArgumentBindingProvider(
                new TableEntityArgumentBindingProvider(),
                new PocoEntityArgumentBindingProvider()); // Supports all types; must come after other providers
        }

        public async Task<IBinding> TryCreateAsync(BindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;
            var tableAttribute = TypeUtility.GetResolvedAttribute<TableAttribute>(context.Parameter);

            if (tableAttribute == null)
            {
                return null;
            }

            string tableName = Resolve(tableAttribute.TableName);
            IStorageAccount account = await _accountProvider.GetStorageAccountAsync(tableAttribute, context.CancellationToken, _nameResolver);
            // requires storage account with table support
            account.AssertTypeOneOf(StorageAccountType.GeneralPurpose);

            StorageClientFactoryContext clientFactoryContext = new StorageClientFactoryContext
            {
                Parameter = context.Parameter
            };
            IStorageTableClient client = account.CreateTableClient(clientFactoryContext);

            bool bindsToEntireTable = tableAttribute.RowKey == null;
            IBinding binding;

            if (bindsToEntireTable)
            {
                // This should have been caught by the other rule-based binders. 
                // We never expect this to get thrown. 
                throw new InvalidOperationException("Can't bind Table to type '" + parameter.ParameterType + "'.");
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

        private string Resolve(string name)
        {
            if (_nameResolver == null)
            {
                return name;
            }

            return _nameResolver.ResolveWholeString(name);
        }
    }
}