﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;
using Microsoft.Azure.WebJobs.Host.Bindings.Runtime;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    public class BlobBinding : FunctionBinding
    {
        private readonly BindingTemplate _pathBindingTemplate;

        public BlobBinding(ScriptHostConfiguration config, BlobBindingMetadata metadata, FileAccess access) : base(config, metadata, access)
        {
            if (string.IsNullOrEmpty(metadata.Path))
            {
                throw new ArgumentException("The blob path cannot be null or empty.");
            }

            Path = metadata.Path;
            _pathBindingTemplate = BindingTemplate.FromString(Path);
        }

        public string Path { get; private set; }

        public override bool HasBindingParameters
        {
            get
            {
                return _pathBindingTemplate.ParameterNames.Any();
            }
        }

        public override async Task BindAsync(BindingContext context)
        {
            string boundBlobPath = Path;
            if (context.BindingData != null)
            {
                boundBlobPath = _pathBindingTemplate.Bind(context.BindingData);
            }

            boundBlobPath = Resolve(boundBlobPath);

            var attribute = new BlobAttribute(boundBlobPath, Access);
            Attribute[] additionalAttributes = null;
            if (!string.IsNullOrEmpty(Metadata.Connection))
            {
                additionalAttributes = new Attribute[]
                {
                    new StorageAccountAttribute(Metadata.Connection)
                };
            }

            RuntimeBindingContext runtimeContext = new RuntimeBindingContext(attribute, additionalAttributes);
            await BindStreamAsync(context.Value, Access, context.Binder, runtimeContext);
        }

        public override Collection<CustomAttributeBuilder> GetCustomAttributes()
        {
            Collection<CustomAttributeBuilder> attributes = new Collection<CustomAttributeBuilder>();

            var constructorTypes = new Type[] { typeof(string), typeof(FileAccess) };
            var constructorArguments = new object[] { Path, Access };
            var attribute = new CustomAttributeBuilder(typeof(BlobAttribute).GetConstructor(constructorTypes), constructorArguments);

            attributes.Add(attribute);

            if (!string.IsNullOrEmpty(Metadata.Connection))
            {
                AddStorageAccountAttribute(attributes, Metadata.Connection);
            }

            return attributes;
        }
    }
}
