using System;
using System.Linq;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Tables
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

            public IValueProvider Bind(CloudTable value, FunctionBindingContext context)
            {
                IQueryable<TElement> queryable;

                if (!value.Exists())
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
