using System.Collections.Generic;

namespace Microsoft.Azure.Jobs.Host.Triggers
{
    internal class CompositeTriggerBindingProvider : ITriggerBindingProvider
    {
        private readonly IEnumerable<ITriggerBindingProvider> _providers;

        public CompositeTriggerBindingProvider(params ITriggerBindingProvider[] providers)
        {
            _providers = providers;
        }

        public ITriggerBinding TryCreate(TriggerBindingProviderContext context)
        {
            foreach (ITriggerBindingProvider provider in _providers)
            {
                ITriggerBinding binding = provider.TryCreate(context);

                if (binding != null)
                {
                    return binding;
                }
            }

            return null;
        }
    }
}
