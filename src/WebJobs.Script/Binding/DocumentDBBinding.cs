// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    internal class DocumentDBBinding : FunctionBinding
    {
        private BindingDirection _bindingDirection;

        public DocumentDBBinding(ScriptHostConfiguration config, string name, string databaseName, string collectionName, bool createIfNotExists, FileAccess access, BindingDirection direction) :
            base(config, name, BindingType.DocumentDB, access, false)
        {
            DatabaseName = databaseName;
            CollectionName = collectionName;
            CreateIfNotExists = createIfNotExists;
            _bindingDirection = direction;
        }

        public string DatabaseName { get; private set; }

        public string CollectionName { get; private set; }

        public bool CreateIfNotExists { get; private set; }

        public override bool HasBindingParameters
        {
            get
            {
                return false;
            }
        }

        public override CustomAttributeBuilder GetCustomAttribute()
        {
            Type attributeType = typeof(DocumentDBAttribute);
            PropertyInfo[] props = new[]
            {
                attributeType.GetProperty("DatabaseName"),
                attributeType.GetProperty("CollectionName"),
                attributeType.GetProperty("CreateIfNotExists")
            };

            object[] propValues = new object[]
            {
                DatabaseName,
                CollectionName,
                CreateIfNotExists
            };

            ConstructorInfo constructor = attributeType.GetConstructor(System.Type.EmptyTypes);

            return new CustomAttributeBuilder(constructor, new object[] { }, props, propValues);
        }

        public override async Task BindAsync(BindingContext context)
        {
            DocumentDBAttribute attribute = new DocumentDBAttribute
            {
                DatabaseName = DatabaseName,
                CollectionName = CollectionName,
                CreateIfNotExists = CreateIfNotExists
            };

            // Only output bindings are supported.
            if (Access == FileAccess.Write && _bindingDirection == BindingDirection.Out)
            {
                IAsyncCollector<JObject> collector = context.Binder.Bind<IAsyncCollector<JObject>>(attribute);
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
