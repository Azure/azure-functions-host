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
            _bindingDirection = metadata.Direction;
        }

        public string DatabaseName { get; private set; }

        public string CollectionName { get; private set; }

        public bool CreateIfNotExists { get; private set; }

        public string ConnectionString { get; private set; }

        public override bool HasBindingParameters
        {
            get
            {
                return false;
            }
        }

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
                attributeType.GetProperty("ConnectionString")
            };

            object[] propValues = new object[]
            {
                CreateIfNotExists,
                ConnectionString
            };

            ConstructorInfo constructor = attributeType.GetConstructor(new[] { typeof(string), typeof(string) });
            return new Collection<CustomAttributeBuilder>()
            {
                new CustomAttributeBuilder(constructor, constructorValues, props, propValues)
            };
        }

        public override async Task BindAsync(BindingContext context)
        {
            // Only output bindings are supported.
            if (Access == FileAccess.Write && _bindingDirection == BindingDirection.Out)
            {
                DocumentDBAttribute attribute = new DocumentDBAttribute(DatabaseName, CollectionName)
                {
                    CreateIfNotExists = CreateIfNotExists,
                    ConnectionString = ConnectionString
                };

                RuntimeBindingContext runtimeContext = new RuntimeBindingContext(attribute);
                IAsyncCollector<JObject> collector = await context.Binder.BindAsync<IAsyncCollector<JObject>>(runtimeContext);
                byte[] bytes;
                using (MemoryStream ms = new MemoryStream())
                {
                    context.Value.CopyTo(ms);
                    bytes = ms.ToArray();
                }
                JObject entity = JObject.Parse(Encoding.UTF8.GetString(bytes));
                await collector.AddAsync(entity);
            }
        }
    }
}
