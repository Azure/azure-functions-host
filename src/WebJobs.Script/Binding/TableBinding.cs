// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;
using Microsoft.Azure.WebJobs.Host.Bindings.Runtime;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.WindowsAzure.Storage;
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
        private readonly TableQuery _tableQuery;

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

            _tableQuery = new TableQuery
            {
                TakeCount = metadata.Take ?? 50,
                FilterString = metadata.Filter
            };
        }

        public string TableName { get; private set; }
        public string PartitionKey { get; private set; }
        public string RowKey { get; private set; }

        public override bool HasBindingParameters
        {
            get
            {
                return (_partitionKeyBindingTemplate != null && _partitionKeyBindingTemplate.ParameterNames.Any()) ||
                       (_rowKeyBindingTemplate != null && _rowKeyBindingTemplate.ParameterNames.Any());
            }
        }

        public override Collection<CustomAttributeBuilder> GetCustomAttributes()
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
                constructorTypes = new Type[] { typeof(string), typeof(string), typeof(string) };
                constructorArguments = new object[] { TableName, PartitionKey, RowKey };
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
            if (context.BindingData != null)
            {
                if (_partitionKeyBindingTemplate != null)
                {
                    boundPartitionKey = _partitionKeyBindingTemplate.Bind(context.BindingData);
                }
                
                if (_rowKeyBindingTemplate != null)
                {
                    boundRowKey = _rowKeyBindingTemplate.Bind(context.BindingData);
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

            Attribute[] additionalAttributes = null;
            if (!string.IsNullOrEmpty(Metadata.Connection))
            {
                additionalAttributes = new Attribute[]
                {
                    new StorageAccountAttribute(Metadata.Connection)
                };
            }

            if (Access == FileAccess.Write)
            {
                RuntimeBindingContext runtimeContext = new RuntimeBindingContext(new TableAttribute(TableName), additionalAttributes);
                IAsyncCollector<DynamicTableEntity> collector = await context.Binder.BindAsync<IAsyncCollector<DynamicTableEntity>>(runtimeContext);
                ICollection<JToken> entities = ReadAsCollection(context.Value);

                foreach (JObject entity in entities)
                {
                    // any key values specified on the entity override any values
                    // specified in the binding
                    string keyValue = (string)entity["partitionKey"];
                    if (!string.IsNullOrEmpty(keyValue))
                    {
                        boundPartitionKey = Resolve(keyValue);
                        entity.Remove("partitionKey");
                    }

                    keyValue = (string)entity["rowKey"];
                    if (!string.IsNullOrEmpty(keyValue))
                    {
                        boundRowKey = Resolve(keyValue);
                        entity.Remove("rowKey");
                    }

                    DynamicTableEntity tableEntity = new DynamicTableEntity(boundPartitionKey, boundRowKey);
                    foreach (JProperty property in entity.Properties())
                    {
                        EntityProperty entityProperty = EntityProperty.CreateEntityPropertyFromObject((object)property.Value);
                        tableEntity.Properties.Add(property.Name, entityProperty);
                    }

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
                    RuntimeBindingContext runtimeContext = new RuntimeBindingContext(new TableAttribute(TableName, boundPartitionKey, boundRowKey), additionalAttributes);
                    DynamicTableEntity tableEntity = await context.Binder.BindAsync<DynamicTableEntity>(runtimeContext);
                    if (tableEntity != null)
                    {
                        json = ConvertEntityToJObject(tableEntity).ToString();
                    }
                }
                else
                {
                    // binding to entire table (query multiple table entities)
                    RuntimeBindingContext runtimeContext = new RuntimeBindingContext(new TableAttribute(TableName, boundPartitionKey, boundRowKey), additionalAttributes);
                    CloudTable table = await context.Binder.BindAsync<CloudTable>(runtimeContext);
                    var entities = table.ExecuteQuery(_tableQuery);

                    JArray entityArray = new JArray();
                    foreach (var entity in entities)
                    {
                        entityArray.Add(ConvertEntityToJObject(entity));
                    }

                    json = entityArray.ToString(Formatting.None);
                }

                if (json != null)
                {
                    // We're explicitly NOT disposing the StreamWriter because
                    // we don't want to close the underlying Stream
                    StreamWriter sw = new StreamWriter(context.Value);
                    await sw.WriteAsync(json);
                    sw.Flush();
                }
            }
        }

        private static JObject ConvertEntityToJObject(DynamicTableEntity tableEntity)
        {
            OperationContext context = new OperationContext();
            var entityProperties = tableEntity.WriteEntity(context);

            JObject jsonObject = new JObject();
            foreach (var entityProperty in entityProperties)
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
            return jsonObject;
        }
    }
}
