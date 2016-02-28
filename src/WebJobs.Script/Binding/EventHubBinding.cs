// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    public class EventHubBinding : FunctionBinding
    {
        private readonly BindingTemplate _eventHubNameBindingTemplate;

        public EventHubBinding(ScriptHostConfiguration config, string name, string eventHubName, FileAccess access, bool isTrigger) : 
            base(config, name, "eventhub", access, isTrigger)
        {
            EventHubName = eventHubName;
            _eventHubNameBindingTemplate = BindingTemplate.FromString(EventHubName);
        }

        public string EventHubName { get; private set; }

        public override bool HasBindingParameters
        {
            get
            {
                return _eventHubNameBindingTemplate.ParameterNames.Any();
            }
        }

        public override CustomAttributeBuilder GetCustomAttribute()
        {
            var constructorTypes = new Type[] { typeof(string) };
            var constructorArguments = new object[] { EventHubName };

            return new CustomAttributeBuilder(typeof(ServiceBus.EventHubAttribute).GetConstructor(constructorTypes), constructorArguments);
        }

        public override async Task BindAsync(BindingContext context)
        {
            string eventHubName = this.EventHubName;
            if (context.BindingData != null)
            {
                eventHubName = _eventHubNameBindingTemplate.Bind(context.BindingData);
            }

            eventHubName = Resolve(eventHubName);

            // only an output binding is supported
            IAsyncCollector<byte[]> collector = context.Binder.Bind<IAsyncCollector<byte[]>>(new ServiceBus.EventHubAttribute(eventHubName));
            byte[] bytes;
            using (MemoryStream ms = new MemoryStream())
            {
                context.Value.CopyTo(ms);
                bytes = ms.ToArray();
            }
            await collector.AddAsync(bytes);
        }
    }
}
