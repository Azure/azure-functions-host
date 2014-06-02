using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.Host.Tables
{
    internal class CompositeEntityArgumentBindingProvider : ITableEntityArgumentBindingProvider
    {
        private readonly IEnumerable<ITableEntityArgumentBindingProvider> _providers;

        public CompositeEntityArgumentBindingProvider(params ITableEntityArgumentBindingProvider[] providers)
        {
            _providers = providers;
        }

        public IArgumentBinding<TableEntityContext> TryCreate(Type parameterType)
        {
            foreach (ITableEntityArgumentBindingProvider provider in _providers)
            {
                IArgumentBinding<TableEntityContext> binding = provider.TryCreate(parameterType);

                if (binding != null)
                {
                    return binding;
                }
            }

            return null;
        }
    }
}
