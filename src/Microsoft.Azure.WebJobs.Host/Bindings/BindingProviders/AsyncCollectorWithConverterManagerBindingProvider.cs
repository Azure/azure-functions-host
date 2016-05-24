// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Protocols;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    // Bind Attribute --> IAsyncCollector<TMessage>
    // USe the converter manager to coerce from the user parameter type to TMessage.
    internal class AsyncCollectorWithConverterManagerBindingProvider<TAttribute, TMessage> :
        IBindingProvider
        where TAttribute : Attribute
    {
        private readonly INameResolver _nameResolver;
        private readonly IConverterManager _converterManager;
        private readonly Func<TAttribute, IAsyncCollector<TMessage>> _buildFromAttribute;
        private readonly Func<TAttribute, ParameterInfo, INameResolver, ParameterDescriptor> _buildParamDescriptor;
        private readonly Func<TAttribute, ParameterInfo, INameResolver, Task<TAttribute>> _postResolveHook;

        public AsyncCollectorWithConverterManagerBindingProvider(
            INameResolver nameResolver,
            IConverterManager converterManager,
            Func<TAttribute, IAsyncCollector<TMessage>> buildFromAttribute,
            Func<TAttribute, ParameterInfo, INameResolver, ParameterDescriptor> buildParamDescriptor = null,
            Func<TAttribute, ParameterInfo, INameResolver, Task<TAttribute>> postResolveHook = null)
        {
            this._nameResolver = nameResolver;
            this._buildFromAttribute = buildFromAttribute;
            this._converterManager = converterManager;
            this._buildParamDescriptor = buildParamDescriptor;
            this._postResolveHook = postResolveHook;
        }

        // Called once per method definition. Very static. 
        public Task<IBinding> TryCreateAsync(BindingProviderContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            ParameterInfo parameter = context.Parameter;
            TAttribute attribute = parameter.GetCustomAttribute<TAttribute>(inherit: false);

            if (attribute == null)
            {
                return Task.FromResult<IBinding>(null);
            }

            IBinding binding = BindingFactoryHelpers.BindCollector<TAttribute, TMessage>(
                parameter,
                _nameResolver,
                _converterManager,
                context.BindingDataContract,
                _buildFromAttribute,
                _buildParamDescriptor,
                _postResolveHook);

            return Task.FromResult(binding);
        }
    }
}