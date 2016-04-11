// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    // Useful for extension  that need some special case type restriction. 
    // If the user parameter type passes the predicate, then chain to an inner an provider. 
    internal class FilteringBindingProvider : IBindingProvider
    {
        private readonly Func<Type, bool> _predicate;
        private readonly IBindingProvider _inner;

        public FilteringBindingProvider(Func<Type, bool> predicate, IBindingProvider inner)
        {
            _predicate = predicate;
            _inner = inner;
        }

        public Task<IBinding> TryCreateAsync(BindingProviderContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }
            Type parametertype = context.Parameter.ParameterType;
            if (!_predicate(parametertype))
            {
                return Task.FromResult<IBinding>(null);
            }
            return _inner.TryCreateAsync(context);
        }
    }
}