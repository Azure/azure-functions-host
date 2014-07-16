// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.Jobs.Host.Triggers
{
    internal class CompositeTriggerBindingProvider : ITriggerBindingProvider
    {
        private readonly IEnumerable<ITriggerBindingProvider> _providers;

        public CompositeTriggerBindingProvider(IEnumerable<ITriggerBindingProvider> providers)
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
