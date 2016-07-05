// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    [CLSCompliant(false)]
    public class TableBinding : FunctionBinding
    {
        private readonly BindingTemplate _partitionKeyBindingTemplate;
        private readonly BindingTemplate _rowKeyBindingTemplate;
        private readonly BindingTemplate _filterBindingTemplate;

        public TableBinding(ScriptHostConfiguration config, TableBindingMetadata metadata, FileAccess access) 
            : base(config, metadata, access)
        {
            if (string.IsNullOrEmpty(metadata.TableName))
            {
                throw new ArgumentException("The table name cannot be null or empty.");
            }

            TableName = metadata.TableName;

            PartitionKey = metadata.PartitionKey;
            if (!string.IsNullOrEmpty(PartitionKey))
            {
                _partitionKeyBindingTemplate = BindingTemplate.FromString(PartitionKey);
            }

            RowKey = metadata.RowKey;
            if (!string.IsNullOrEmpty(RowKey))
            {
                _rowKeyBindingTemplate = BindingTemplate.FromString(RowKey);
            }

            Filter = metadata.Filter;
            if (!string.IsNullOrEmpty(Filter))
            {
                _filterBindingTemplate = BindingTemplate.FromString(Filter);
            }

            Take = metadata.Take ?? 50;
        }

        public string TableName { get; private set; }
        public string PartitionKey { get; private set; }
        public string RowKey { get; private set; }
        public int Take { get; private set; }
        public string Filter { get; private set; }

        public override Collection<CustomAttributeBuilder> GetCustomAttributes(Type parameterType)
        {
            Collection<CustomAttributeBuilder> attributes = new Collection<CustomAttributeBuilder>();

            Type[] constructorTypes = null;
            object[] constructorArguments = null;
            if (Access == FileAccess.Write)
            {
                constructorTypes = new Type[] { typeof(string) };
                constructorArguments = new object[] { TableName };
            }
            else
            {
                if (!string.IsNullOrEmpty(PartitionKey) && !string.IsNullOrEmpty(RowKey))
                {
                    constructorTypes = new Type[] { typeof(string), typeof(string), typeof(string) };
                    constructorArguments = new object[] { TableName, PartitionKey, RowKey };
                }
                else
                {
                    constructorTypes = new Type[] { typeof(string) };
                    constructorArguments = new object[] { TableName };
                }
            }

            attributes.Add(new CustomAttributeBuilder(typeof(TableAttribute).GetConstructor(constructorTypes), constructorArguments));

            if (!string.IsNullOrEmpty(Metadata.Connection))
            {
                AddStorageAccountAttribute(attributes, Metadata.Connection);
            }

            return attributes;
        }

        public override async Task BindAsync(BindingContext context)
        {
            string boundPartitionKey = PartitionKey;
            string boundRowKey = RowKey;
            string boundFilter = Filter;

            IReadOnlyDictionary<string, string> bindingData = null;
            if (context.BindingData != null)
            {
                bindingData = context.BindingData.ToStringValues();
            }

            if (context.BindingData != null)
            {
                if (_partitionKeyBindingTemplate != null)
                {
                    boundPartitionKey = _partitionKeyBindingTemplate.Bind(bindingData);
                }
                
                if (_rowKeyBindingTemplate != null)
                {
                    boundRowKey = _rowKeyBindingTemplate.Bind(bindingData);
                }

                if (_filterBindingTemplate != null)
                {
                    boundFilter = _filterBindingTemplate.Bind(bindingData);
                }
            }

            if (!string.IsNullOrEmpty(boundPartitionKey))
            {
                boundPartitionKey = Resolve(boundPartitionKey);
            }

            if (!string.IsNullOrEmpty(boundRowKey))
            {
                boundRowKey = Resolve(boundRowKey);
            }

            if (!string.IsNullOrEmpty(boundFilter))
            {
                boundFilter = Resolve(boundFilter);
            }

            Collection<Attribute> attributes = new Collection<Attribute>();
            if (!string.IsNullOrEmpty(Metadata.Connection))
            {
                attributes.Add(new StorageAccountAttribute(Metadata.Connection));
            }

            if (Access == FileAccess.Write)
            {
                attributes.Insert(0, new TableAttribute(TableName));
                IAsyncCollector<DynamicTableEntity> collector = await context.Binder.BindAsync<IAsyncCollector<DynamicTableEntity>>(attributes.ToArray());
                ICollection entities = ReadAsCollection(context.Value);

                foreach (JObject entity in entities)
                {
                    // Here we're mapping from JObject to DynamicTableEntity because the Table binding doesn't support
                    // a JObject binding. We enable that for the core Table binding in the future, which would allow
                    // this code to go away.
                    DynamicTableEntity tableEntity = CreateTableEntityFromJObject(boundPartitionKey, boundRowKey, entity);
                    await collector.AddAsync(tableEntity);
                }
            }
            else
            {
                string json = null;
                if (!string.IsNullOrEmpty(boundPartitionKey) &&
                    !string.IsNullOrEmpty(boundRowKey))
                {
                    // singleton
                    attributes.Insert(0, new TableAttribute(TableName, boundPartitionKey, boundRowKey));
                    DynamicTableEntity tableEntity = await context.Binder.BindAsync<DynamicTableEntity>(attributes.ToArray());
                    if (tableEntity != null)
                    {
                        json = ConvertEntityToJObject(tableEntity).ToString();
                    }
                }
                else
                {
                    // binding to multiple table entities
                    attributes.Insert(0, new TableAttribute(TableName));
                    CloudTable table = await context.Binder.BindAsync<CloudTable>(attributes.ToArray());

                    string finalQuery = boundFilter;
                    if (!string.IsNullOrEmpty(boundPartitionKey))
                    {
                        var partitionKeyPredicate = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, boundPartitionKey);
                        if (!string.IsNullOrEmpty(boundFilter))
                        {
                            finalQuery = TableQuery.CombineFilters(boundFilter, TableOperators.And, partitionKeyPredicate);
                        }
                        else
                        {
                            finalQuery = partitionKeyPredicate;
                        }
                    }

                    TableQuery tableQuery = new TableQuery
                    {
                        TakeCount = Take,
                        FilterString = finalQuery
                    };

                    var entities = table.ExecuteQuery(tableQuery);
                    JArray entityArray = new JArray();
                    foreach (var entity in entities)
                    {
                        entityArray.Add(ConvertEntityToJObject(entity));
                    }

                    json = entityArray.ToString(Formatting.None);
                }

                if (json != null)
                {
                    if (context.DataType == DataType.Stream)
                    {
                        // We're explicitly NOT disposing the StreamWriter because
                        // we don't want to close the underlying Stream
                        StreamWriter sw = new StreamWriter((Stream)context.Value);
                        await sw.WriteAsync(json);
                        sw.Flush();
                    }
                    else
                    {
                        context.Value = json;
                    }
                }
            }
        }

        private DynamicTableEntity CreateTableEntityFromJObject(string partitionKey, string rowKey, JObject entity)
        {
            // any key values specified on the entity override any values
            // specified in the binding
            JToken keyValue = null;
            if (entity.TryGetValue("partitionKey", StringComparison.OrdinalIgnoreCase, out keyValue))
            {
                partitionKey = Resolve((string)keyValue);
                entity.Remove("partitionKey");
            }

            if (entity.TryGetValue("rowKey", StringComparison.OrdinalIgnoreCase, out keyValue))
            {
                rowKey = Resolve((string)keyValue);
                entity.Remove("rowKey");
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
    }
}
