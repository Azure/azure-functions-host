// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Bindings.Runtime
{
    internal class RuntimeBinding : IBinding
    {
        private readonly string _parameterName;

        public RuntimeBinding(string parameterName)
        {
            _parameterName = parameterName;
        }

        public bool FromAttribute
        {
            get { return false; }
        }

        private Task<IValueProvider> BindAsync(IAttributeBindingSource binding, ValueBindingContext context)
        {
            IValueProvider provider = new RuntimeValueProvider(binding);
            return Task.FromResult(provider);
        }

        public Task<IValueProvider> BindAsync(object value, ValueBindingContext context)
        {
            IAttributeBindingSource binding = value as IAttributeBindingSource;

            if (binding == null)
            {
                throw new InvalidOperationException("Unable to convert value to IAttributeBinding.");
            }

            return BindAsync(binding, context);
        }

        public Task<IValueProvider> BindAsync(BindingContext context)
        {
            return BindAsync(new AttributeBindingSource(context.AmbientContext), context.ValueContext);
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            return new BinderParameterDescriptor
            {
                Name = _parameterName
            };
        }
    }
}
