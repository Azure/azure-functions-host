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
    public class EventHubBinding : FunctionBinding
    {
        private readonly BindingTemplate _eventHubNameBindingTemplate;

        public EventHubBinding(ScriptHostConfiguration config, EventHubBindingMetadata metadata, FileAccess access) : 
            base(config, metadata, access)
        {
            if (string.IsNullOrEmpty(metadata.Path))
            {
                throw new ArgumentException("The event hub path cannot be null or empty.");
            }

            EventHubName = metadata.Path;
            _eventHubNameBindingTemplate = BindingTemplate.FromString(EventHubName);
        }

        public string EventHubName { get; private set; }

        public override Collection<CustomAttributeBuilder> GetCustomAttributes(Type parameterType)
        {
            var constructorTypes = new Type[] { typeof(string) };
            var constructorArguments = new object[] { EventHubName };

            return new Collection<CustomAttributeBuilder>
            {
                new CustomAttributeBuilder(typeof(ServiceBus.EventHubAttribute).GetConstructor(constructorTypes), constructorArguments)
            };
        }

        public override async Task BindAsync(BindingContext context)
        {
            string eventHubName = this.EventHubName;
            if (context.BindingData != null)
            {
                eventHubName = _eventHubNameBindingTemplate.Bind(context.BindingData);
            }

            eventHubName = Resolve(eventHubName);

            var attribute = new ServiceBus.EventHubAttribute(eventHubName);
            RuntimeBindingContext runtimeContext = new RuntimeBindingContext(attribute);

            await BindAsyncCollectorAsync<string>(context.Value, context.Binder, runtimeContext);
        }
    }
}
