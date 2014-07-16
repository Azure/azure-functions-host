// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Converters;

namespace Microsoft.Azure.Jobs.Host.Bindings.Data
{
    internal class StructDataBindingProvider<TBindingData> : IBindingProvider
        where TBindingData : struct
    {
        private static readonly IDataArgumentBindingProvider<TBindingData> _innerProvider =
            new CompositeArgumentBindingProvider<TBindingData>(
                new ConverterArgumentBindingProvider<TBindingData, TBindingData>(new IdentityConverter<TBindingData>()),
                new ConverterArgumentBindingProvider<TBindingData, string>(new TToStringConverter<TBindingData>()));

        public IBinding TryCreate(BindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;

            IArgumentBinding<TBindingData> argumentBinding = _innerProvider.TryCreate(parameter);

            string parameterName = parameter.Name;
            Type parameterType = parameter.ParameterType;

            if (argumentBinding == null)
            {
                throw new InvalidOperationException(
                    "Can't bind parameter '" + parameterName + "' to type '" + parameterType + "'.");
            }

            return new StructDataBinding<TBindingData>(parameterName, argumentBinding);
        }
    }
}
