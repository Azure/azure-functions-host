// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;
using Microsoft.Azure.WebJobs.Host.Bindings.Runtime;
using Microsoft.Azure.WebJobs.Script.Description;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    internal class DocumentDBBinding : FunctionBinding
    {
        private readonly BindingTemplate _databaseNameBindingTemplate;
        private readonly BindingTemplate _collectionNameBindingTemplate;
        private readonly BindingTemplate _partitionKeyBindingTemplate;
        private readonly BindingTemplate _idBindingTemplate;
        private BindingDirection _bindingDirection;

        public DocumentDBBinding(ScriptHostConfiguration config, DocumentDBBindingMetadata metadata, FileAccess access) :
            base(config, metadata, access)
        {
            DatabaseName = metadata.DatabaseName;
            if (!string.IsNullOrEmpty(DatabaseName))
            {
                _databaseNameBindingTemplate = BindingTemplate.FromString(DatabaseName);
            }

            CollectionName = metadata.CollectionName;
            if (!string.IsNullOrEmpty(CollectionName))
            {
                _collectionNameBindingTemplate = BindingTemplate.FromString(CollectionName);
            }

            Id = metadata.Id;
            if (!string.IsNullOrEmpty(Id))
            {
                _idBindingTemplate = BindingTemplate.FromString(Id);
            }

            PartitionKey = metadata.PartitionKey;
            if (!string.IsNullOrEmpty(PartitionKey))
            {
                _partitionKeyBindingTemplate = BindingTemplate.FromString(PartitionKey);
            }

            CreateIfNotExists = metadata.CreateIfNotExists;
            ConnectionString = metadata.Connection;
            CollectionThroughput = metadata.CollectionThroughput;

            _bindingDirection = metadata.Direction;
        }

        public string DatabaseName { get; private set; }

        public string CollectionName { get; private set; }

        public bool CreateIfNotExists { get; private set; }

        public string ConnectionString { get; private set; }

        public string Id { get; private set; }

        public string PartitionKey { get; private set; }

        public int CollectionThroughput { get; private set; }

        public override Collection<CustomAttributeBuilder> GetCustomAttributes(Type parameterType)
        {
            Type attributeType = typeof(DocumentDBAttribute);

            object[] constructorValues = new object[]
            {
                DatabaseName,
                CollectionName
            };

            PropertyInfo[] props = new[]
            {
                attributeType.GetProperty("CreateIfNotExists"),
                attributeType.GetProperty("ConnectionString"),
                attributeType.GetProperty("Id"),
                attributeType.GetProperty("PartitionKey"),
                attributeType.GetProperty("CollectionThroughput")
            };

            object[] propValues = new object[]
            {
                CreateIfNotExists,
                ConnectionString,
                Id,
                PartitionKey,
                CollectionThroughput
            };

            ConstructorInfo constructor = attributeType.GetConstructor(new[] { typeof(string), typeof(string) });
            return new Collection<CustomAttributeBuilder>()
            {
                new CustomAttributeBuilder(constructor, constructorValues, props, propValues)
            };
        }

        public override async Task BindAsync(BindingContext context)
        {
            string boundDatabaseName = ResolveBindingTemplate(DatabaseName, _databaseNameBindingTemplate, context.BindingData);
            string boundCollectionName = ResolveBindingTemplate(CollectionName, _collectionNameBindingTemplate, context.BindingData);
            string boundId = ResolveBindingTemplate(Id, _idBindingTemplate, context.BindingData);
            string boundPartitionKey = ResolveBindingTemplate(PartitionKey, _partitionKeyBindingTemplate, context.BindingData);

            DocumentDBAttribute attribute = new DocumentDBAttribute(boundDatabaseName, boundCollectionName)
            {
                CreateIfNotExists = CreateIfNotExists,
                ConnectionString = ConnectionString,
                Id = boundId,
                PartitionKey = boundPartitionKey,
                CollectionThroughput = CollectionThroughput
            };
            RuntimeBindingContext runtimeContext = new RuntimeBindingContext(attribute);

            if (Access == FileAccess.Read && _bindingDirection == BindingDirection.In)
            {
                context.Value = await context.Binder.BindAsync<JObject>(runtimeContext);
            }
            else if (Access == FileAccess.Write && _bindingDirection == BindingDirection.Out)
            {
                await BindAsyncCollectorAsync<JObject>(context, runtimeContext);
            }
        }
    }
}
