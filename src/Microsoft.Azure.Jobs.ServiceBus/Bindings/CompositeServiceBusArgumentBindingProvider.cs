using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.ServiceBus.Bindings
{
    internal class CompositeServiceBusArgumentBindingProvider : IServiceBusArgumentBindingProvider
    {
        private readonly IEnumerable<IServiceBusArgumentBindingProvider> _providers;

        public CompositeServiceBusArgumentBindingProvider(params IServiceBusArgumentBindingProvider[] providers)
        {
            _providers = providers;
        }

        public IArgumentBinding<ServiceBusEntity> TryCreate(ParameterInfo parameter)
        {
            foreach (IServiceBusArgumentBindingProvider provider in _providers)
            {
                IArgumentBinding<ServiceBusEntity> binding = provider.TryCreate(parameter);

                if (binding != null)
                {
                    return binding;
                }
            }

            return null;
        }
    }
}
