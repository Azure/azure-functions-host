// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Converters;

namespace Microsoft.Azure.Jobs.Host.Bindings.Data
{
    internal class ConverterArgumentBindingProvider<TBindingData, T> : IDataArgumentBindingProvider<TBindingData>
    {
        private readonly IConverter<TBindingData, T> _converter;

        public ConverterArgumentBindingProvider(IConverter<TBindingData, T> converter)
        {
            _converter = converter;
        }

        public IArgumentBinding<TBindingData> TryCreate(ParameterInfo parameter)
        {
            if (parameter.ParameterType != typeof(T))
            {
                return null;
            }

            return new ConverterArgumentBinding(_converter);
        }

        internal class ConverterArgumentBinding : IArgumentBinding<TBindingData>
        {
            private readonly IConverter<TBindingData, T> _converter;

            public ConverterArgumentBinding(IConverter<TBindingData, T> converter)
            {
                _converter = converter;
            }

            public Type ValueType
            {
                get { return typeof(T); }
            }

            public IValueProvider Bind(TBindingData value, FunctionBindingContext context)
            {
                object converted = _converter.Convert(value);
                return new ObjectValueProvider(converted, typeof(T));
            }
        }
    }
}
