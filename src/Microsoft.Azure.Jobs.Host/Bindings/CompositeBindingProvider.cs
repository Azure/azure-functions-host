// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal class CompositeBindingProvider : IBindingProvider
    {
        readonly IEnumerable<IBindingProvider> _providers;

        public CompositeBindingProvider(IEnumerable<IBindingProvider> providers)
        {
            _providers = providers;
        }

        public async Task<IBinding> TryCreateAsync(BindingProviderContext context)
        {
            foreach (IBindingProvider provider in _providers)
            {
                IBinding binding = await provider.TryCreateAsync(context);

                if (binding != null)
                {
                    return binding;
                }
            }

            return null;
        }
    }
}
