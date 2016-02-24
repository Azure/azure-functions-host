// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    internal class EventHubAttributeBindingProvider : IBindingProvider
    {
        private INameResolver _nameResolver;
        private IEventHubProvider _eventHubConfig;
        private IConverterManager _converterManager;

        public EventHubAttributeBindingProvider(INameResolver nameResolver, IConverterManager converterManager, IEventHubProvider _eventHubConfig)
        {
            this._nameResolver = nameResolver;
            this._eventHubConfig = _eventHubConfig;
            this._converterManager = converterManager;
        }

        public Task<IBinding> TryCreateAsync(BindingProviderContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            ParameterInfo parameter = context.Parameter;
            EventHubAttribute attribute = parameter.GetCustomAttribute<EventHubAttribute>(inherit: false);

            if (attribute == null)
            {
                return Task.FromResult<IBinding>(null);
            }

            string name = attribute.EventHubName;
            var resolvedName = _nameResolver.ResolveWholeString(name);

            Func<string, EventHubClient> invokeStringBinder = (invokeString) => _eventHubConfig.GetEventHubClient(invokeString);

            IBinding binding = GenericBinder.BindCollector<EventData, EventHubClient>(
                parameter,
                _converterManager,
                (client, valueBindingContext) => new EventHubAsyncCollector(client),
                resolvedName,
                invokeStringBinder);

            return Task.FromResult(binding);
        }
    }
}