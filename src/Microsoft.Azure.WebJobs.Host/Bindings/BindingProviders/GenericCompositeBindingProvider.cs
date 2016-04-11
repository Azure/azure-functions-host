// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    // Composite binder for a specific attribute. 
    // Ignore parameters that don't have the attribute - other binders will get them. 
    // If it does have the attribue, but none of the binders handle it, then throw an error. 
    internal class GenericCompositeBindingProvider<TAttribute> : IBindingProvider
        where TAttribute : Attribute
    {
        private readonly IEnumerable<IBindingProvider> _providers;

        private readonly Action<TAttribute> _validator;

        public GenericCompositeBindingProvider(Action<TAttribute> validator, params IBindingProvider[] providers)
        {
            _providers = providers;
            _validator = validator;
        }

        public GenericCompositeBindingProvider(IEnumerable<IBindingProvider> providers)
        {
            _providers = providers;
        }

        public async Task<IBinding> TryCreateAsync(BindingProviderContext context)
        {
            var attr = context.Parameter.GetCustomAttribute<TAttribute>();

            if (attr == null)
            {
                return null;
            }

            if (_validator != null)
            {
                _validator(attr);
            }

            foreach (IBindingProvider provider in _providers)
            {
                IBinding binding = await provider.TryCreateAsync(context);
                if (binding != null)
                {
                    return binding;
                }
            }

            // Nobody claimed it.                 
            throw new InvalidOperationException("Can't bind to parameter.");
        }
    }
}
