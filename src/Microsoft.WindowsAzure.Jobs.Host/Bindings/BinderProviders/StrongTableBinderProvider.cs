using System;
using AzureTables;

namespace Microsoft.WindowsAzure.Jobs
{
    // Check for IAzureTableReader<T>, and if so, create an AzureTable<T>.
    class StrongTableBinderProvider : ICloudTableBinderProvider
    {
        class TableBinder<T> : ICloudTableBinder where T : new()
        {
            public BindResult Bind(IBinderEx bindingContext, Type targetType, string tableName)
            {
                CloudStorageAccount account = Utility.GetAccount(bindingContext.AccountConnectionString);

                string accountConnectionString = bindingContext.AccountConnectionString;
                AzureTable<T> table = TableProviderTestHook.Default.Create<T>(accountConnectionString, tableName);                               

                return new BindCleanupResult 
                { 
                    Result = table,
                    Cleanup = () => table.Flush()
                };
            }
        }

        public ICloudTableBinder TryGetBinder(Type targetType, bool isReadOnly)
        {
            if (targetType.IsGenericType)
            {
                if (targetType.GetGenericTypeDefinition() == typeof(IAzureTableReader<>))
                {
                    var args = targetType.GetGenericArguments();
                    var tkey = args[0];

                    var t = typeof(TableBinder<>).MakeGenericType(tkey);
                    var binder = Activator.CreateInstance(t);
                    return (ICloudTableBinder) binder;
                }
            }
            return null;
        }
    }
}
