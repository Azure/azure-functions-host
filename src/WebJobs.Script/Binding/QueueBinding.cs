// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;
using Microsoft.Azure.WebJobs.Host.Bindings.Runtime;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    public class QueueBinding : FunctionBinding
    {
        private readonly BindingTemplate _queueNameBindingTemplate;

        public QueueBinding(ScriptHostConfiguration config, QueueBindingMetadata metadata, FileAccess access) : 
            base(config, metadata, access)
        {
            if (string.IsNullOrEmpty(metadata.QueueName))
            {
                throw new ArgumentException("The queue name cannot be null or empty.");
            }

            QueueName = metadata.QueueName;
            _queueNameBindingTemplate = BindingTemplate.FromString(QueueName);
        }

        public string QueueName { get; private set; }

        public override Collection<CustomAttributeBuilder> GetCustomAttributes(Type parameterType)
        {
            Collection<CustomAttributeBuilder> attributes = new Collection<CustomAttributeBuilder>();

            var constructorTypes = new Type[] { typeof(string) };
            var constructorArguments = new object[] { QueueName };

            attributes.Add(new CustomAttributeBuilder(typeof(QueueAttribute).GetConstructor(constructorTypes), constructorArguments));

            if (!string.IsNullOrEmpty(Metadata.Connection))
            {
                AddStorageAccountAttribute(attributes, Metadata.Connection);
            }

            return attributes;
        }

        public override async Task BindAsync(BindingContext context)
        {
            string boundQueueName = QueueName;
            if (context.BindingData != null)
            {
                boundQueueName = _queueNameBindingTemplate.Bind(context.BindingData);
            }

            boundQueueName = Resolve(boundQueueName);
            
            var attribute = new QueueAttribute(boundQueueName);
            Attribute[] additionalAttributes = null;
            if (!string.IsNullOrEmpty(Metadata.Connection))
            {
                additionalAttributes = new Attribute[]
                {
                    new StorageAccountAttribute(Metadata.Connection)
                };
            }
            RuntimeBindingContext runtimeContext = new RuntimeBindingContext(attribute, additionalAttributes);

            await BindAsyncCollectorAsync<byte[]>(context, runtimeContext);
        }
    }
}
