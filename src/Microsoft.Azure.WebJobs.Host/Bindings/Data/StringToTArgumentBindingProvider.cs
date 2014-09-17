// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Bindings.Data
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

            return (IArgumentBinding<TBindingData>)new StringToTArgumentBinding(parameterType);
        }

        private class StringToTArgumentBinding : IArgumentBinding<string>
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

            public Task<IValueProvider> BindAsync(string value, ValueBindingContext context)
            {
                object converted = ObjectBinderHelpers.BindFromString(value, _valueType);
                IValueProvider provider = new ObjectValueProvider(converted, _valueType);
                return Task.FromResult(provider);
            }
        }
    }
}
