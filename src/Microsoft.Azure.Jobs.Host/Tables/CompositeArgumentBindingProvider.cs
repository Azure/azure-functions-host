using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Tables
{
    internal class CompositeArgumentBindingProvider : ITableArgumentBindingProvider
    {
        private readonly IEnumerable<ITableArgumentBindingProvider> _providers;

        public CompositeArgumentBindingProvider(params ITableArgumentBindingProvider[] providers)
        {
            _providers = providers;
        }

        public IArgumentBinding<CloudTable> TryCreate(Type parameterType)
        {
            foreach (ITableArgumentBindingProvider provider in _providers)
            {
                IArgumentBinding<CloudTable> binding = provider.TryCreate(parameterType);

                if (binding != null)
                {
                    return binding;
                }
            }

            return null;
        }
    }
}
