// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Converters;
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

        private TableAttributeBindingProvider(INameResolver nameResolver, IStorageAccountProvider accountProvider, IExtensionRegistry extensions)
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

            _entityBindingProvider =
                new CompositeEntityArgumentBindingProvider(
                new TableEntityArgumentBindingProvider(),
                new PocoEntityArgumentBindingProvider()); // Supports all types; must come after other providers
        }

        // [Table] has some pre-existing behavior where the storage account can be specified outside of the [Table] attribute. 
        // The storage account is pulled from the ParameterInfo (which could pull in a [Storage] attribute on the container class)
        // Resolve everything back down to a single attribute so we can use the binding helpers. 
        // This pattern should be rare since other extensions can just keep everything directly on the primary attribute. 
        private async Task<TableAttribute> CollectAttributeInfo(TableAttribute attrResolved, ParameterInfo parameter, INameResolver nameResolver)
        {
            // Look for [Storage] attribute and squirrel over 
            IStorageAccount account = await _accountProvider.GetStorageAccountAsync(parameter, CancellationToken.None, nameResolver);
            StorageClientFactoryContext clientFactoryContext = new StorageClientFactoryContext
            {
                Parameter = parameter
            };
            IStorageTableClient client = account.CreateTableClient(clientFactoryContext);

            return new ResolvedTableAttribute(attrResolved, client);
        }

        public static IBindingProvider Build(INameResolver nameResolver, IConverterManager converterManager, IStorageAccountProvider accountProvider, IExtensionRegistry extensions)
        {
            var original = new TableAttributeBindingProvider(nameResolver, accountProvider, extensions);

            converterManager.AddConverter<JObject, ITableEntity, TableAttribute>(original.JObjectToTableEntityConverterFunc);            
            converterManager.AddConverter<object, ITableEntity, TableAttribute>(typeof(ObjectToITableEntityConverter<>));

            // IStorageTable --> IQueryable<ITableEntity>
            converterManager.AddConverter<IStorageTable, IQueryable<OpenType>, TableAttribute>(typeof(TableToIQueryableConverter<>));
            
            var bindingFactory = new BindingFactory(nameResolver, converterManager);

            // Includes converter manager, which provides access to IQueryable<ITableEntity>
            var bindToExactCloudTable = bindingFactory.BindToInput<TableAttribute, CloudTable>(typeof(JObjectBuilder))
                .SetPostResolveHook<TableAttribute>(original.ToParameterDescriptorForCollector, original.CollectAttributeInfo);

            var bindToExactTestCloudTable = bindingFactory.BindToInput<TableAttribute, IStorageTable>(typeof(JObjectBuilder))
                .SetPostResolveHook<TableAttribute>(original.ToParameterDescriptorForCollector, original.CollectAttributeInfo);

            var bindAsyncCollector = bindingFactory.BindToCollector<TableAttribute, ITableEntity>(original.BuildFromTableAttribute)
                .SetPostResolveHook<TableAttribute>(null, original.CollectAttributeInfo);

            var bindToJobject = bindingFactory.BindToInput<TableAttribute, JObject>(typeof(JObjectBuilder))
                .SetPostResolveHook<TableAttribute>(null, original.CollectAttributeInfo);

            var bindToJArray = bindingFactory.BindToInput<TableAttribute, JArray>(typeof(JObjectBuilder))
                .SetPostResolveHook<TableAttribute>(null, original.CollectAttributeInfo);

            var bindingProvider = new GenericCompositeBindingProvider<TableAttribute>(
                ValidateAttribute, nameResolver,
                new IBindingProvider[]
                {
                    bindAsyncCollector,
                    AllowMultipleRows(bindingFactory, bindToExactCloudTable),
                    AllowMultipleRows(bindingFactory, bindToExactTestCloudTable),
                    bindToJArray,
                    bindToJobject,
                    original
                });

            return bindingProvider;
        }
        
        // Binding rule only allowed on attributes that don't specify the RowKey. 
        private static IBindingProvider AllowMultipleRows(BindingFactory bf, IBindingProvider innerProvider)
        {
            return bf.AddFilter<TableAttribute>((attr, type) => attr.RowKey == null, innerProvider);
        }

        private ParameterDescriptor ToParameterDescriptorForCollector(TableAttribute attribute, ParameterInfo parameter, INameResolver nameResolver)
        {
            Task<IStorageAccount> t = Task.Run(() =>
                _accountProvider.GetStorageAccountAsync(parameter, CancellationToken.None, nameResolver));
            IStorageAccount account = t.GetAwaiter().GetResult();
            string accountName = account.Credentials.AccountName;

            return new TableParameterDescriptor
            {
                Name = parameter.Name,
                AccountName = accountName,
                TableName = Resolve(attribute.TableName),
                Access = FileAccess.ReadWrite
            };
        }

        private static void ValidateAttribute(TableAttribute attribute, Type parameterType)
        {
            // Queue pre-existing  behavior: if there are { }in the path, then defer validation until runtime. 
            if (!attribute.TableName.Contains("{"))
            {
                TableClient.ValidateAzureTableName(attribute.TableName);
            }
        }

        private IAsyncCollector<ITableEntity> BuildFromTableAttribute(TableAttribute attribute)
        {
            IStorageTable table = GetTable(attribute);

            var writer = new TableEntityWriter<ITableEntity>(table);
            return writer;
        }

        // Get the storage table from the attribute.
        private static IStorageTable GetTable(TableAttribute attribute)
        {
            var tableClient = ((ResolvedTableAttribute)attribute).Client;                           
            IStorageTable table = tableClient.GetTableReference(attribute.TableName);
            return table;
        }

        private ITableEntity JObjectToTableEntityConverterFunc(JObject source, TableAttribute attribute)
        {
            var result = this.CreateTableEntityFromJObject(attribute.PartitionKey, attribute.RowKey, source);
            return result;
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
            IStorageAccount account = await _accountProvider.GetStorageAccountAsync(context.Parameter, context.CancellationToken, _nameResolver);
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

        private string Resolve(string queueName)
        {
            if (_nameResolver == null)
            {
                return queueName;
            }

            return _nameResolver.ResolveWholeString(queueName);
        }

        private static JObject ConvertEntityToJObject(DynamicTableEntity tableEntity)
        {
            JObject jsonObject = new JObject();
            foreach (var entityProperty in tableEntity.Properties)
            {
                JValue value = null;
                switch (entityProperty.Value.PropertyType)
                {
                    case EdmType.String:
                        value = new JValue(entityProperty.Value.StringValue);
                        break;
                    case EdmType.Int32:
                        value = new JValue(entityProperty.Value.Int32Value);
                        break;
                    case EdmType.Int64:
                        value = new JValue(entityProperty.Value.Int64Value);
                        break;
                    case EdmType.DateTime:
                        value = new JValue(entityProperty.Value.DateTime);
                        break;
                    case EdmType.Boolean:
                        value = new JValue(entityProperty.Value.BooleanValue);
                        break;
                    case EdmType.Guid:
                        value = new JValue(entityProperty.Value.GuidValue);
                        break;
                    case EdmType.Double:
                        value = new JValue(entityProperty.Value.DoubleValue);
                        break;
                    case EdmType.Binary:
                        value = new JValue(entityProperty.Value.BinaryValue);
                        break;
                }

                jsonObject.Add(entityProperty.Key, value);
            }

            jsonObject.Add("PartitionKey", tableEntity.PartitionKey);
            jsonObject.Add("RowKey", tableEntity.RowKey);

            return jsonObject;
        }

        private DynamicTableEntity CreateTableEntityFromJObject(string partitionKey, string rowKey, JObject entity)
        {
            // any key values specified on the entity override any values
            // specified in the binding
            JProperty keyProperty = entity.Properties().SingleOrDefault(p => string.Compare(p.Name, "partitionKey", StringComparison.OrdinalIgnoreCase) == 0);
            if (keyProperty != null)
            {
                partitionKey = Resolve((string)keyProperty.Value);
                entity.Remove(keyProperty.Name);
            }

            keyProperty = entity.Properties().SingleOrDefault(p => string.Compare(p.Name, "rowKey", StringComparison.OrdinalIgnoreCase) == 0);
            if (keyProperty != null)
            {
                rowKey = Resolve((string)keyProperty.Value);
                entity.Remove(keyProperty.Name);
            }

            DynamicTableEntity tableEntity = new DynamicTableEntity(partitionKey, rowKey);
            foreach (JProperty property in entity.Properties())
            {
                EntityProperty entityProperty = CreateEntityPropertyFromJProperty(property);
                tableEntity.Properties.Add(property.Name, entityProperty);
            }

            return tableEntity;
        }

        private static EntityProperty CreateEntityPropertyFromJProperty(JProperty property)
        {
            switch (property.Value.Type)
            {
                case JTokenType.String:
                    return EntityProperty.GeneratePropertyForString((string)property.Value);
                case JTokenType.Integer:
                    return EntityProperty.GeneratePropertyForInt((int)property.Value);
                case JTokenType.Boolean:
                    return EntityProperty.GeneratePropertyForBool((bool)property.Value);
                case JTokenType.Guid:
                    return EntityProperty.GeneratePropertyForGuid((Guid)property.Value);
                case JTokenType.Float:
                    return EntityProperty.GeneratePropertyForDouble((double)property.Value);
                default:
                    return EntityProperty.CreateEntityPropertyFromObject((object)property.Value);
            }
        }

        // Table attributes can optionally be paired with a separate [StorageAccount]. 
        // Consolidate the information from both attributes into a single attribute.
        // New extensions should just place everything in the attribute or the configuration and so shouldn't need to do this. 
        internal sealed class ResolvedTableAttribute : TableAttribute
        {
            public ResolvedTableAttribute(TableAttribute inner, IStorageTableClient client)
                : base(inner.TableName, inner.PartitionKey, inner.RowKey)
            {
                this.Take = inner.Take;
                this.Filter = inner.Filter;
                this.Client = client;
            }

            internal IStorageTableClient Client { get; private set; }
        }

        // IStorageTable --> IQueryable<T>
        // ConverterManager's pattern matcher will figure out TElement. 
        private class TableToIQueryableConverter<TElement> : 
            IAsyncConverter<IStorageTable, IQueryable<TElement>> 
            where TElement : ITableEntity, new()
        {
            public TableToIQueryableConverter()
            {
                // We're now commited to an IQueryable. Verify other constraints. 
                Type entityType = typeof(TElement);

                if (!TableClient.ImplementsITableEntity(entityType))
                {
                    throw new InvalidOperationException("IQueryable is only supported on types that implement ITableEntity.");
                }

                TableClient.VerifyDefaultConstructor(entityType);
            }

            public async Task<IQueryable<TElement>> ConvertAsync(IStorageTable value, CancellationToken cancellation)
            {
                // If Table does not exist, treat it like have zero rows. 
                // This means return an non-null but empty enumerable.
                // SDK doesn't do that, so we need to explicitly check. 
                bool exists = await value.ExistsAsync(CancellationToken.None);

                if (!exists)
                {
                    return Enumerable.Empty<TElement>().AsQueryable();
                }
                else
                {
                    return value.CreateQuery<TElement>();
                }
            }
        }

        // Convert from T --> ITableEntity
        private class ObjectToITableEntityConverter<TElement>
            : IConverter<TElement, ITableEntity>
        {
            private static readonly IConverter<TElement, ITableEntity> Converter = PocoToTableEntityConverter<TElement>.Create();

            public ObjectToITableEntityConverter()
            {
                // JObject case should have been claimed by another converter. 
                // So we can statically enforce an ITableEntity compatible contract
                var t = typeof(TElement);
                TableClient.VerifyContainsProperty(t, "RowKey");
                TableClient.VerifyContainsProperty(t, "PartitionKey");
            }

            public ITableEntity Convert(TElement item)
            {
                return Converter.Convert(item);
            }
        }

        // Provide some common builder rules. 
        private class JObjectBuilder : 
            IAsyncConverter<TableAttribute, CloudTable>,
            IConverter<TableAttribute, IStorageTable>,
            IAsyncConverter<TableAttribute, JObject>,
            IAsyncConverter<TableAttribute, JArray>
        {
            IStorageTable IConverter<TableAttribute, IStorageTable>.Convert(TableAttribute attribute)
            {
                IStorageTable table = GetTable(attribute);
                return table;
            }

            async Task<CloudTable> IAsyncConverter<TableAttribute, CloudTable>.ConvertAsync(TableAttribute attribute, CancellationToken cancellation)
            {
                IStorageTable table = GetTable(attribute);
                await table.CreateIfNotExistsAsync(CancellationToken.None);

                var sdkTable = table.SdkObject;
                return sdkTable;
            }

            async Task<JObject> IAsyncConverter<TableAttribute, JObject>.ConvertAsync(TableAttribute attribute, CancellationToken cancellation)
            {
                IStorageTable table = GetTable(attribute);

                IStorageTableOperation retrieve = table.CreateRetrieveOperation<DynamicTableEntity>(
                  attribute.PartitionKey, attribute.RowKey);
                TableResult result = await table.ExecuteAsync(retrieve, CancellationToken.None);
                DynamicTableEntity entity = (DynamicTableEntity)result.Result;
                if (entity == null)
                {
                    return null;
                }
                else
                {
                    var obj = ConvertEntityToJObject(entity);
                    return obj;
                }
            }

            // Build a JArray.
            // Used as an alternative to binding to IQueryable.
            async Task<JArray> IAsyncConverter<TableAttribute, JArray>.ConvertAsync(TableAttribute attribute, CancellationToken cancellation)
            {
                var table = GetTable(attribute).SdkObject;

                string finalQuery = attribute.Filter;
                if (!string.IsNullOrEmpty(attribute.PartitionKey))
                {
                    var partitionKeyPredicate = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, attribute.PartitionKey);
                    if (!string.IsNullOrEmpty(attribute.Filter))
                    {
                        finalQuery = TableQuery.CombineFilters(attribute.Filter, TableOperators.And, partitionKeyPredicate);
                    }
                    else
                    {
                        finalQuery = partitionKeyPredicate;
                    }
                }

                TableQuery tableQuery = new TableQuery
                {
                    FilterString = finalQuery
                };
                if (attribute.Take > 0)
                {
                    tableQuery.TakeCount = attribute.Take;
                }
                int countRemaining = attribute.Take;

                JArray entityArray = new JArray();
                TableContinuationToken token = null;

                do
                {
                    var segment = await table.ExecuteQuerySegmentedAsync(tableQuery, token);
                    var entities = segment.Results;

                    token = segment.ContinuationToken;

                    foreach (var entity in entities)
                    {
                        countRemaining--;
                        entityArray.Add(ConvertEntityToJObject(entity));

                        if (countRemaining == 0)
                        {
                            token = null;
                            break;
                        }
                    }
                }
                while (token != null);

                return entityArray;
            }
        }
    }
}
