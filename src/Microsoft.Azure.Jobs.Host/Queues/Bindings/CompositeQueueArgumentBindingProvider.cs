using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Bindings
{
    internal class CompositeQueueArgumentBindingProvider : IQueueArgumentBindingProvider
    {
        private readonly IEnumerable<IQueueArgumentBindingProvider> _providers;

        public CompositeQueueArgumentBindingProvider(params IQueueArgumentBindingProvider[] providers)
        {
            _providers = providers;
        }

        public IArgumentBinding<CloudQueue> TryCreate(ParameterInfo parameter)
        {
            foreach (IQueueArgumentBindingProvider provider in _providers)
            {
                IArgumentBinding<CloudQueue> binding = provider.TryCreate(parameter);

                if (binding != null)
                {
                    return binding;
                }
            }

            return null;
        }
    }
}
