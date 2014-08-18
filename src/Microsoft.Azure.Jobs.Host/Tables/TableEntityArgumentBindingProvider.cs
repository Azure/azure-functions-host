// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Bindings;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    internal class TableEntityArgumentBindingProvider : ITableEntityArgumentBindingProvider
    {
        public IArgumentBinding<TableEntityContext> TryCreate(Type parameterType)
        {
            if (parameterType.ContainsGenericParameters)
            {
                return null;
            }

            if (!TableClient.ImplementsITableEntity(parameterType))
            {
                return null;
            }

            TableClient.VerifyDefaultConstructor(parameterType);

            return CreateBinding(parameterType);
        }

        private static IArgumentBinding<TableEntityContext> CreateBinding(Type entityType)
        {
            Type genericType = typeof(TableEntityArgumentBinding<>).MakeGenericType(entityType);
            return (IArgumentBinding<TableEntityContext>)Activator.CreateInstance(genericType);
        }
    }
}
