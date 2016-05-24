// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    // Useful for extension  that need some special case type restriction. 
    // If the user parameter type passes the predicate, then chain to an inner an provider. 
    internal class FilteringBindingProvider<TAttribute> : IBindingProvider
        where TAttribute : Attribute
    {
        private readonly Func<TAttribute, Type, bool> _predicate;
        private readonly INameResolver _nameResolver;
        private readonly IBindingProvider _inner;

        public FilteringBindingProvider(
            Func<TAttribute, Type, bool> predicate, 
            INameResolver nameResolver,  
            IBindingProvider inner)
        {
            _predicate = predicate;
            _nameResolver = nameResolver;
            _inner = inner;
        }

        public Task<IBinding> TryCreateAsync(BindingProviderContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }
            Type parametertype = context.Parameter.ParameterType;

            var attr = context.Parameter.GetCustomAttribute<TAttribute>();

            var cloner = new AttributeCloner<TAttribute>(attr, context.BindingDataContract, _nameResolver);
            var attrNameResolved = cloner.GetNameResolvedAttribute();

            // This may do validation and throw too. 
            bool canBind = _predicate(attrNameResolved, parametertype);

            if (!canBind)
            {
                return Task.FromResult<IBinding>(null);
            }
            return _inner.TryCreateAsync(context);
        }
    }
}