// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    public class ServiceBusBinding : FunctionBinding
    {
        private readonly BindingTemplate _queueOrTopicNameBindingTemplate;

        public ServiceBusBinding(ScriptHostConfiguration config, string name, string queueOrTopicName, FileAccess access, bool isTrigger) : base(config, name, BindingType.ServiceBus, access, isTrigger)
        {
            if (string.IsNullOrEmpty(queueOrTopicName))
            {
                throw new ArgumentException("A valid queue or topic name must be specified.");
            }

            QueueOrTopicName = queueOrTopicName;
            _queueOrTopicNameBindingTemplate = BindingTemplate.FromString(QueueOrTopicName);
        }

        public string QueueOrTopicName { get; private set; }

        public override bool HasBindingParameters
        {
            get
            {
                return _queueOrTopicNameBindingTemplate.ParameterNames.Any();
            }
        }

        public override CustomAttributeBuilder GetCustomAttribute()
        {
            var constructorTypes = new Type[] { typeof(string) };
            var constructorArguments = new object[] { QueueOrTopicName };

            return new CustomAttributeBuilder(typeof(ServiceBusAttribute).GetConstructor(constructorTypes), constructorArguments);
        }

        public override async Task BindAsync(BindingContext context)
        {
            string boundQueueName = QueueOrTopicName;
            if (context.BindingData != null)
            {
                boundQueueName = _queueOrTopicNameBindingTemplate.Bind(context.BindingData);
            }

            boundQueueName = Resolve(boundQueueName);

            // TODO: Need to handle Stream conversions properly
            Stream valueStream = context.Value as Stream;

            // only an output binding is supported
            using (StreamReader reader = new StreamReader(valueStream))
            {
                // TODO: only string supported currently - need to support other types
                IAsyncCollector<string> collector = context.Binder.Bind<IAsyncCollector<string>>(new ServiceBusAttribute(boundQueueName));
                string data = reader.ReadToEnd();
                await collector.AddAsync(data);
            }
        }
    }
}
