// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Converters;

namespace Microsoft.Azure.Jobs.Host.Bindings.Data
{
    internal class DataBindingProvider : IBindingProvider
    {
        public IBinding TryCreate(BindingProviderContext context)
        {
            IReadOnlyDictionary<string, Type> bindingDataContract = context.BindingDataContract;
            string parameterName = context.Parameter.Name;

            if (bindingDataContract == null || !bindingDataContract.ContainsKey(parameterName))
            {
                return null;
            }

            Type bindingDataType = bindingDataContract[parameterName];

            if (bindingDataType.IsByRef)
            {
                return null;
            }

            IBindingProvider typedProvider = CreateTypedBindingProvider(bindingDataType);
            return typedProvider.TryCreate(context);
        }

        private static IBindingProvider CreateTypedBindingProvider(Type bindingDataType)
        {
            Type genericTypeDefinition;

            if (!bindingDataType.IsValueType)
            {
                genericTypeDefinition = typeof(ClassDataBindingProvider<>);
            }
            else
            {
                genericTypeDefinition = typeof(StructDataBindingProvider<>);
            }

            Type genericType = genericTypeDefinition.MakeGenericType(bindingDataType);
            return (IBindingProvider)Activator.CreateInstance(genericType);
        }
    }
}
