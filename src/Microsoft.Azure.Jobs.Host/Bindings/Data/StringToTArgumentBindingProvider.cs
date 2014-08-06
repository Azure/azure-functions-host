// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading.Tasks;

namespace Microsoft.Azure.Jobs.Host.Bindings.Data
{
    internal class StringToTArgumentBindingProvider<TBindingData> : IDataArgumentBindingProvider<TBindingData>
    {
        public IArgumentBinding<TBindingData> TryCreate(ParameterInfo parameter)
        {
            if (typeof(TBindingData) != typeof(string))
            {
                return null;
            }

            Type parameterType = parameter.ParameterType;

            if (!ObjectBinderHelpers.CanBindFromString(parameterType))
            {
                return null;
            }

            return new StringToTArgumentBinding(parameterType);
        }

        private class StringToTArgumentBinding : IArgumentBinding<TBindingData>
        {
            private readonly Type _valueType;

            public StringToTArgumentBinding(Type valueType)
            {
                _valueType = valueType;
            }

            public Type ValueType
            {
                get { return _valueType; }
            }

            public Task<IValueProvider> BindAsync(TBindingData value, ValueBindingContext context)
            {
                string text = value.ToString(); // Really (string)input, but the compiler can't verify that's possible.
                object converted = ObjectBinderHelpers.BindFromString(text, _valueType);
                IValueProvider provider = new ObjectValueProvider(converted, _valueType);
                return Task.FromResult(provider);
            }
        }
    }
}
