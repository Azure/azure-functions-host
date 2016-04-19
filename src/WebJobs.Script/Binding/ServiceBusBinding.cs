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
    public class ServiceBusBinding : FunctionBinding
    {
        private readonly BindingTemplate _queueOrTopicNameBindingTemplate;

        public ServiceBusBinding(ScriptHostConfiguration config, ServiceBusBindingMetadata metadata, FileAccess access) : 
            base(config, metadata, access)
        {
            string queueOrTopicName = metadata.QueueName ?? metadata.TopicName;
            if (string.IsNullOrEmpty(queueOrTopicName))
            {
                throw new ArgumentException("A valid queue or topic name must be specified.");
            }

            QueueOrTopicName = queueOrTopicName;
            _queueOrTopicNameBindingTemplate = BindingTemplate.FromString(QueueOrTopicName);
        }

        public string QueueOrTopicName { get; private set; }

        public override Collection<CustomAttributeBuilder> GetCustomAttributes()
        {
            var constructorTypes = new Type[] { typeof(string) };
            var constructorArguments = new object[] { QueueOrTopicName };

            var attributes = new Collection<CustomAttributeBuilder>
            {
                new CustomAttributeBuilder(typeof(ServiceBusAttribute).GetConstructor(constructorTypes), constructorArguments)
            };

            if (!string.IsNullOrEmpty(Metadata.Connection))
            {
                AddServiceBusAccountAttribute(attributes, Metadata.Connection);
            }

            return attributes;
        }

        public override async Task BindAsync(BindingContext context)
        {
            string boundQueueName = QueueOrTopicName;
            if (context.BindingData != null)
            {
                boundQueueName = _queueOrTopicNameBindingTemplate.Bind(context.BindingData);
            }

            boundQueueName = Resolve(boundQueueName);

            var attribute = new ServiceBusAttribute(boundQueueName);
            Attribute[] additionalAttributes = null;
            if (!string.IsNullOrEmpty(Metadata.Connection))
            {
                additionalAttributes = new Attribute[]
                {
                    new ServiceBusAccountAttribute(Metadata.Connection)
                };
            }
            RuntimeBindingContext runtimeContext = new RuntimeBindingContext(attribute, additionalAttributes);

            await BindAsyncCollectorAsync<string>(context.Value, context.Binder, runtimeContext);
        }

        internal static void AddServiceBusAccountAttribute(Collection<CustomAttributeBuilder> attributes, string connection)
        {
            var constructorTypes = new Type[] { typeof(string) };
            var constructorArguments = new object[] { connection };
            var attribute = new CustomAttributeBuilder(typeof(ServiceBusAccountAttribute).GetConstructor(constructorTypes), constructorArguments);
            attributes.Add(attribute);
        }
    }
}
