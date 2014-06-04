using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Triggers
{
    internal class CompositeArgumentBindingProvider : IQueueTriggerArgumentBindingProvider
    {
        private readonly IEnumerable<IQueueTriggerArgumentBindingProvider> _providers;

        public CompositeArgumentBindingProvider(params IQueueTriggerArgumentBindingProvider[] providers)
        {
            _providers = providers;
        }

        public IArgumentBinding<CloudQueueMessage> TryCreate(ParameterInfo parameter)
        {
            foreach (IQueueTriggerArgumentBindingProvider provider in _providers)
            {
                IArgumentBinding<CloudQueueMessage> binding = provider.TryCreate(parameter);

                if (binding != null)
                {
                    return binding;
                }
            }

            return null;
        }
    }
}
