using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Triggers
{
    internal class CompositeArgumentBindingProvider : IQueueTriggerArgumentBindingProvider
    {
        private readonly IEnumerable<IQueueTriggerArgumentBindingProvider> _providers;

        public CompositeArgumentBindingProvider(params IQueueTriggerArgumentBindingProvider[] providers)
        {
            _providers = providers;
        }

        public IArgumentBinding<BrokeredMessage> TryCreate(ParameterInfo parameter)
        {
            foreach (IQueueTriggerArgumentBindingProvider provider in _providers)
            {
                IArgumentBinding<BrokeredMessage> binding = provider.TryCreate(parameter);

                if (binding != null)
                {
                    return binding;
                }
            }

            return null;
        }
    }
}
