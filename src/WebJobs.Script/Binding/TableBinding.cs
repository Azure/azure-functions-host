// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class TableBinding : Binding
    {
        private readonly BindingTemplate _partitionKeyBindingTemplate;
        private readonly BindingTemplate _rowKeyBindingTemplate;

        public TableBinding(JobHostConfiguration config, string name, string tableName, string partitionKey, string rowKey, FileAccess fileAccess) : base(config, name, "queue", fileAccess, false)
        {
            TableName = tableName;
            PartitionKey = partitionKey;
            RowKey = rowKey;
            _partitionKeyBindingTemplate = BindingTemplate.FromString(PartitionKey);
            _rowKeyBindingTemplate = BindingTemplate.FromString(RowKey);
        }

        public string TableName { get; private set; }
        public string PartitionKey { get; private set; }
        public string RowKey { get; private set; }

        public override bool HasBindingParameters
        {
            get
            {
                return _partitionKeyBindingTemplate.ParameterNames.Any() ||
                       _rowKeyBindingTemplate.ParameterNames.Any();
            }
        }

        public override async Task BindAsync(IBinder binder, Stream stream, IReadOnlyDictionary<string, string> bindingData)
        {
            string boundPartitionKey = PartitionKey;
            string boundRowKey = RowKey;
            if (bindingData != null)
            {
                boundPartitionKey = _partitionKeyBindingTemplate.Bind(bindingData);
                boundRowKey = _rowKeyBindingTemplate.Bind(bindingData);
            }

            boundPartitionKey = Resolve(boundPartitionKey);
            boundRowKey = Resolve(boundRowKey);

            if (FileAccess == FileAccess.Write)
            {
                // read the content as a JObject
                JObject jsonObject = null;
                using (StreamReader streamReader = new StreamReader(stream))
                {
                    string content = await streamReader.ReadToEndAsync();
                    jsonObject = JObject.Parse(content);
                }

                // TODO: If RowKey has not been specified in the binding, try to
                // derive from the object properties (e.g. "rowKey" or "id" properties);

                IAsyncCollector<DynamicTableEntity> collector = binder.Bind<IAsyncCollector<DynamicTableEntity>>(new TableAttribute(TableName));
                DynamicTableEntity tableEntity = new DynamicTableEntity(boundPartitionKey, boundRowKey);
                foreach (JProperty property in jsonObject.Properties())
                {
                    EntityProperty entityProperty = EntityProperty.CreateEntityPropertyFromObject((object)property.Value);
                    tableEntity.Properties.Add(property.Name, entityProperty);
                }

                await collector.AddAsync(tableEntity);
            }
            else
            {
                DynamicTableEntity tableEntity = binder.Bind<DynamicTableEntity>(new TableAttribute(TableName, boundPartitionKey, boundRowKey));
                if (tableEntity != null)
                {
                    OperationContext context = new OperationContext();
                    var entityProperties = tableEntity.WriteEntity(context);

                    JObject jsonObject = new JObject();
                    foreach (var entityProperty in entityProperties)
                    {
                        jsonObject.Add(entityProperty.Key, entityProperty.Value.StringValue);
                    }
                    string json = jsonObject.ToString();

                    using (StreamWriter sw = new StreamWriter(stream))
                    {
                        await sw.WriteAsync(json);
                    }
                }
            }
        }
    }
}
