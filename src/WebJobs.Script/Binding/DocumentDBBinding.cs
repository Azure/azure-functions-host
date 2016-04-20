// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings.Runtime;
using Microsoft.Azure.WebJobs.Script.Description;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    internal class DocumentDBBinding : FunctionBinding
    {
        private BindingDirection _bindingDirection;

        public DocumentDBBinding(ScriptHostConfiguration config, DocumentDBBindingMetadata metadata, FileAccess access) :
            base(config, metadata, access)
        {
            DatabaseName = metadata.DatabaseName;
            CollectionName = metadata.CollectionName;
            CreateIfNotExists = metadata.CreateIfNotExists;
            ConnectionString = metadata.Connection;
            Id = metadata.Id;
            PartitionKey = metadata.PartitionKey;
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

        public override Collection<CustomAttributeBuilder> GetCustomAttributes()
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
            DocumentDBAttribute attribute = new DocumentDBAttribute(DatabaseName, CollectionName)
            {
                CreateIfNotExists = CreateIfNotExists,
                ConnectionString = ConnectionString,
                Id = Id,
                PartitionKey = PartitionKey,
                CollectionThroughput = CollectionThroughput
            };
            RuntimeBindingContext runtimeContext = new RuntimeBindingContext(attribute);

            if (Access == FileAccess.Read && _bindingDirection == BindingDirection.In)
            {
                JObject input = await context.Binder.BindAsync<JObject>(runtimeContext);
                if (input != null)
                {
                    byte[] byteArray = Encoding.UTF8.GetBytes(input.ToString());
                    using (MemoryStream stream = new MemoryStream(byteArray))
                    {
                        stream.CopyTo(context.Value);
                    }
                }
            }
            else if (Access == FileAccess.Write && _bindingDirection == BindingDirection.Out)
            {
                await BindAsyncCollectorAsync<JObject>(context.Value, context.Binder, runtimeContext);
            }
        }
    }
}
