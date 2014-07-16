// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
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

        private IValueProvider Bind(IAttributeBindingSource binding, FunctionBindingContext context)
        {
            return new RuntimeValueProvider(binding);
        }

        public IValueProvider Bind(object value, FunctionBindingContext context)
        {
            IAttributeBindingSource binding = value as IAttributeBindingSource;

            if (binding == null)
            {
                throw new InvalidOperationException("Unable to convert value to IAttributeBinding.");
            }

            return Bind(binding, context);
        }

        public IValueProvider Bind(BindingContext context)
        {
            return Bind(new AttributeBindingSource(context), context.FunctionContext);
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
