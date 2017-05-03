// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    internal class CompositeBindingProvider : IBindingProvider, IRuleProvider
    {
        private readonly IEnumerable<IBindingProvider> _providers;

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

        public IEnumerable<Rule> GetRules()
        {
            foreach (var provider in _providers.OfType<IRuleProvider>())
            {
                foreach (var rule in provider.GetRules())
                {
                    yield return rule;
                }
            }
        }

        public Type GetDefaultType(Attribute attribute, FileAccess access, Type requestedType)
        {
            foreach (var provider in _providers.OfType<IRuleProvider>())
            {
                var type = provider.GetDefaultType(attribute, access, requestedType);
                if (type != null)
                {
                    return type;
                }
            }
            return null;
        }
    }
}
