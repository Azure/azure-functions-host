// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    internal class QueryableArgumentBindingProvider : ITableArgumentBindingProvider
    {
        public IArgumentBinding<CloudTable> TryCreate(Type parameterType)
        {
            if (!parameterType.IsGenericType || parameterType.GetGenericTypeDefinition() != typeof(IQueryable<>))
            {
                return null;
            }

            Type entityType = GetQueryableItemType(parameterType);

            if (!TableClient.ImplementsITableEntity(entityType))
            {
                throw new InvalidOperationException("IQueryable is only supported on types that implement ITableEntity.");
            }

            TableClient.VerifyDefaultConstructor(entityType);

            return CreateBinding(entityType);
        }

        private static Type GetQueryableItemType(Type queryableType)
        {
            Type[] genericArguments = queryableType.GetGenericArguments();
            var itemType = genericArguments[0];
            return itemType;
        }

        private static IArgumentBinding<CloudTable> CreateBinding(Type entityType)
        {
            Type genericType = typeof(QueryableArgumentBinding<>).MakeGenericType(entityType);
            return (IArgumentBinding<CloudTable>)Activator.CreateInstance(genericType);
        }

        private class QueryableArgumentBinding<TElement> : IArgumentBinding<CloudTable>
            where TElement : ITableEntity, new()
        {
            public Type ValueType
            {
                get { return typeof(IQueryable<TElement>); }
            }

            public async Task<IValueProvider> BindAsync(CloudTable value, ValueBindingContext context)
            {
                IQueryable<TElement> queryable;

                if (!await value.ExistsAsync(context.CancellationToken))
                {
                    queryable = Enumerable.Empty<TElement>().AsQueryable();
                }
                else
                {
                    queryable = value.CreateQuery<TElement>();
                }

                return new TableValueProvider(value, queryable, typeof(IQueryable<TElement>));
            }
        }
    }
}
