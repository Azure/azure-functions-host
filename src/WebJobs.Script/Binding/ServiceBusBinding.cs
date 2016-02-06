// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    public class ServiceBusBinding : FunctionBinding
    {
        private readonly BindingTemplate _queueOrTopicNameBindingTemplate;

        public ServiceBusBinding(ScriptHostConfiguration config, string name, string queueOrTopicName, FileAccess fileAccess, bool isTrigger) : base(config, name, "serviceBus", fileAccess, isTrigger)
        {
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
