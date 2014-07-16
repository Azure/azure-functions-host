// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.Host.Tables
{
    internal class PocoEntityArgumentBindingProvider : ITableEntityArgumentBindingProvider
    {
        public IArgumentBinding<TableEntityContext> TryCreate(Type parameterType)
        {
            if (parameterType.IsByRef)
            {
                return null;
            }

            TableClient.VerifyDefaultConstructor(parameterType);

            return CreateBinding(parameterType);
        }

        private static IArgumentBinding<TableEntityContext> CreateBinding(Type entityType)
        {
            Type genericType = typeof(PocoEntityArgumentBinding<>).MakeGenericType(entityType);
            return (IArgumentBinding<TableEntityContext>)Activator.CreateInstance(genericType);
        }
    }
}
