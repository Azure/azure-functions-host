using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal class CompositeArgumentBindingProvider : IBlobArgumentBindingProvider
    {
        private readonly IEnumerable<IBlobArgumentBindingProvider> _providers;

        public CompositeArgumentBindingProvider(IEnumerable<IBlobArgumentBindingProvider> providers)
        {
            _providers = providers;
        }

        public IArgumentBinding<ICloudBlob> TryCreate(Type parameterType)
        {
            foreach (IBlobArgumentBindingProvider provider in _providers)
            {
                IArgumentBinding<ICloudBlob> binding = provider.TryCreate(parameterType);

                if (binding != null)
                {
                    return binding;
                }
            }

            return null;
        }
    }
}
