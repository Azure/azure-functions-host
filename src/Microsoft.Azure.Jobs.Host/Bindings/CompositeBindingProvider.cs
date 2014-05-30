using System.Collections.Generic;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal class CompositeBindingProvider : IBindingProvider
    {
        readonly IEnumerable<IBindingProvider> _providers;

        public CompositeBindingProvider(IEnumerable<IBindingProvider> providers)
        {
            _providers = providers;
        }

        public IBinding TryCreate(BindingProviderContext context)
        {
            foreach (IBindingProvider provider in _providers)
            {
                IBinding binding = provider.TryCreate(context);

                if (binding != null)
                {
                    return binding;
                }
            }

            return null;
        }
    }
}
