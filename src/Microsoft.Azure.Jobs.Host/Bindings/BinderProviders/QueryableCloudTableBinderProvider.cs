using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Bindings.BinderProviders
{
    internal class QueryableCloudTableBinderProvider : ICloudTableBinderProvider
    {
        public ICloudTableBinder TryGetBinder(Type targetType)
        {
            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(IQueryable<>))
            {
                Type entityType = GetQueryableItemType(targetType);
                Debug.Assert(entityType != null);

                if (TableClient.ImplementsITableEntity(entityType))
                {
                    TableClient.VerifyDefaultConstructor(entityType);
                    return TryGetBinderGeneric(entityType);
                }
                else
                {
                    throw new InvalidOperationException("IQueryable is only supported on types that implement ITableEntity.");
                }
            }

            return null;
        }

        private static ICloudTableBinder TryGetBinderGeneric(Type type)
        {
            // Call TryGetBinder<T>();
            MethodInfo genericMethod = typeof(QueryableCloudTableBinderProvider).GetMethod("TryGetBinder", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo methodInfo = genericMethod.MakeGenericMethod(type);
            Func<ICloudTableBinder> invoker =
                (Func<ICloudTableBinder>)Delegate.CreateDelegate(typeof(Func<ICloudTableBinder>), methodInfo);
            return invoker.Invoke();
        }

        private static ICloudTableBinder TryGetBinder<T>() where T : ITableEntity, new()
        {
            return new QueryableCloudTableBinder<T>();
        }

        private static Type GetQueryableItemType(Type queryableType)
        {
            Debug.Assert(queryableType != null);
            Type[] genericArguments = queryableType.GetGenericArguments();
            Debug.Assert(genericArguments != null);
            Debug.Assert(genericArguments.Length == 1);
            var itemType = genericArguments[0];
            return itemType;
        }

        private class QueryableCloudTableBinder<T> : ICloudTableBinder where T : ITableEntity, new()
        {
            public BindResult Bind(IBinderEx bindingContext, Type targetType, string tableName)
            {
                CloudStorageAccount account = Utility.GetAccount(bindingContext.StorageConnectionString);
                CloudTableClient client = account.CreateCloudTableClient();
                CloudTable table = client.GetTableReference(tableName);
                IQueryable<T> queryable = CreateQueryable(table);
                return new BindResult { Result = queryable };
            }

            private static IQueryable<T> CreateQueryable(CloudTable table)
            {
                if (!table.Exists())
                {
                    return Enumerable.Empty<T>().AsQueryable();
                }

                return table.CreateQuery<T>();
            }
        }
    }
}
