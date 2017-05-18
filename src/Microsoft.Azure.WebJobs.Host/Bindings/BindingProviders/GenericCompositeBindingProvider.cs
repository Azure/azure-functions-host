// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    // Composite binder for a specific attribute. 
    // Ignore parameters that don't have the attribute - other binders will get them. 
    // If it does have the attribue, but none of the binders handle it, then throw an error. 
    internal class GenericCompositeBindingProvider<TAttribute> : IBindingProvider, IBindingRuleProvider
        where TAttribute : Attribute
    {
        private readonly IEnumerable<IBindingProvider> _providers;

        private readonly Action<TAttribute, Type> _validator;
        private readonly INameResolver _nameResolver;

        public GenericCompositeBindingProvider(Action<TAttribute, Type> validator, INameResolver nameResolver, params IBindingProvider[] providers)
        {
            _providers = providers;
            _validator = validator;
            _nameResolver = nameResolver;
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
                // Expected this will throw on errors. 
                Type parameterType = context.Parameter.ParameterType;
                var cloner = new AttributeCloner<TAttribute>(attr, context.BindingDataContract, _nameResolver);
                var attrNameResolved = cloner.GetNameResolvedAttribute();
                _validator(attrNameResolved, parameterType);
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
            string resourceName = typeof(TAttribute).Name;
            const string Suffix = "Attribute";
            if (resourceName.EndsWith(Suffix))
            {
                resourceName = resourceName.Substring(0, resourceName.Length - Suffix.Length);
            }
            throw new InvalidOperationException("Can't bind " + resourceName + " to type '" + context.Parameter.ParameterType + "'.");
        }

        public IEnumerable<BindingRule> GetRules()
        {
            return _providers.OfType<IBindingRuleProvider>().SelectMany(rule => rule.GetRules());
        }

        public Type GetDefaultType(Attribute attribute, FileAccess access, Type requestedType)
        {
            if (!(attribute is TAttribute))
            {
                return null;
            }

            foreach (var provider in _providers.OfType<IBindingRuleProvider>())
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
