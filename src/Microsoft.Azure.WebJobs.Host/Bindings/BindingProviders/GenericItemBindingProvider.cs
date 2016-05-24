// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    // Binding provider that can bind to arbitrary user parameter. 
    // It invokes the builder function and passes the resolved attribute and user parameter type. 
    // The builder returns an object that is compatible with the user parameter. 
    internal class GenericItemBindingProvider<TAttribute> : IBindingProvider
        where TAttribute : Attribute
    {
        private readonly Func<TAttribute, Type, Task<object>> _builder;
        private readonly INameResolver _nameResolver;

        public GenericItemBindingProvider(
                Func<TAttribute, Type, Task<object>> builder,
                INameResolver nameResolver)
        {
            _builder = builder;
            _nameResolver = nameResolver;
        }

        public Task<IBinding> TryCreateAsync(BindingProviderContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            ParameterInfo parameter = context.Parameter;
            TAttribute attributeSource = parameter.GetCustomAttribute<TAttribute>(inherit: false);
            if (attributeSource == null)
            {
                return Task.FromResult<IBinding>(null);
            }

            var cloner = new AttributeCloner<TAttribute>(attributeSource, context.BindingDataContract, _nameResolver);

            IBinding binding = new Binding(cloner, parameter, _builder);
            return Task.FromResult(binding);
        }

        private class Binding : BindingBase<TAttribute>
        {
            private readonly ParameterInfo _parameter;
            private readonly Func<TAttribute, Type, Task<object>> _builder;

            public Binding(
                    AttributeCloner<TAttribute> cloner,
                    ParameterInfo parameterInfo,
                    Func<TAttribute, Type, Task<object>> builder)
                : base(cloner, parameterInfo)
            {
                _parameter = parameterInfo;
                _builder = builder;
            }

            protected override async Task<IValueProvider> BuildAsync(TAttribute attrResolved)
            {
                string invokeString = Cloner.GetInvokeString(attrResolved);
                object obj = await _builder(attrResolved, _parameter.ParameterType);

                IValueProvider vp = new ConstantValueProvider(obj, _parameter.ParameterType, invokeString);
                return vp;
            }
        }
    }
}
