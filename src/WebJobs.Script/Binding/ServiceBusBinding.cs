// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class ServiceBusBinding : Binding
    {
        private readonly BindingTemplate _queueOrTopicNameBindingTemplate;

        public ServiceBusBinding(JobHostConfiguration config, string name, string queueOrTopicName, FileAccess fileAccess, bool isTrigger) : base(config, name, "serviceBus", fileAccess, isTrigger)
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

        public override async Task BindAsync(IBinder binder, Stream stream, IReadOnlyDictionary<string, string> bindingData)
        {
            string boundQueueName = QueueOrTopicName;
            if (bindingData != null)
            {
                boundQueueName = _queueOrTopicNameBindingTemplate.Bind(bindingData);
            }

            boundQueueName = Resolve(boundQueueName);

            // only an output binding is supported
            using (StreamReader reader = new StreamReader(stream))
            {
                // TODO: only string supported currently - need to support other types
                IAsyncCollector<string> collector = binder.Bind<IAsyncCollector<string>>(new ServiceBusAttribute(boundQueueName));
                string data = reader.ReadToEnd();
                await collector.AddAsync(data);
            }
        }
    }
}
