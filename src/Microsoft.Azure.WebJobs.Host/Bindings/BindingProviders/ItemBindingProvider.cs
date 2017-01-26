// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    // Binding provider that takes an arbitrary (TAttribute ,Type) --> IValueProvider.
    // The IValueProvider  has an OnCompleted hook, so this rule can be used 
    // for bindings that have some write-back semantics and need a hook to flush. 
    internal class ItemBindingProvider<TAttribute> : IBindingProvider
        where TAttribute : Attribute
    {
        private readonly INameResolver _nameResolver;
        private readonly Func<TAttribute, Type, Task<IValueBinder>> _builder;

        public ItemBindingProvider(
                    INameResolver nameResolver,
                    Func<TAttribute, Type, Task<IValueBinder>> builder)
        {
            this._nameResolver = nameResolver;
            this._builder = builder;
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

            IBinding binding = new Binding(cloner, _builder, parameter);
            return Task.FromResult(binding);
        }

        private class Binding : BindingBase<TAttribute>
        {
            private readonly Func<TAttribute, Type, Task<IValueBinder>> _builder;
            private readonly ParameterInfo _parameter;

            public Binding(
                    AttributeCloner<TAttribute> cloner,
                    Func<TAttribute, Type, Task<IValueBinder>> builder,
                    ParameterInfo parameter)
                : base(cloner, parameter)
            {
                this._builder = builder;
                this._parameter = parameter;
            }

            protected override async Task<IValueProvider> BuildAsync(TAttribute attrResolved, ValueBindingContext context)
            {
                string invokeString = Cloner.GetInvokeString(attrResolved);
                IValueBinder valueBinder = await _builder(attrResolved, _parameter.ParameterType);

                return new Wrapper(valueBinder, invokeString);
            }

            private class Wrapper : IValueBinder
            {
                private readonly IValueBinder _inner;
                private readonly string _invokeString;

                public Wrapper(IValueBinder inner, string invokeString)
                {
                    _inner = inner;
                    _invokeString = invokeString;
                }

                public Type Type
                {
                    get
                    {
                        return _inner.Type;
                    }
                }

                public Task<object> GetValueAsync()
                {
                    return _inner.GetValueAsync();
                }

                public Task SetValueAsync(object value, CancellationToken cancellationToken)
                {
                    return _inner.SetValueAsync(value, cancellationToken);
                }

                public string ToInvokeString()
                {
                    return _invokeString;
                }
            }
        }
    }
}
