using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.ServiceBus.Bindings
{
    internal class CompositeArgumentBindingProvider : IServiceBusArgumentBindingProvider
    {
        private readonly IEnumerable<IServiceBusArgumentBindingProvider> _providers;

        public CompositeArgumentBindingProvider(params IServiceBusArgumentBindingProvider[] providers)
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
