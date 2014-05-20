using System;
using System.Collections.Generic;
using AzureTables;

namespace Microsoft.Azure.Jobs.Host.Bindings.BinderProviders
{
    // Bind IDictionary<Tuple<string,string>, T> to an azure table.
    class DictionaryTableBinderProvider : ICloudTableBinderProvider
    {
        class TableBinder<T> : ICloudTableBinder where T : new()
        {
            public BindResult Bind(IBinderEx bindingContext, Type targetType, string tableName)
            {
                string accountConnectionString = bindingContext.StorageConnectionString;
                AzureTable<T> table = TableProviderTestHook.Default.Create<T>(accountConnectionString, tableName);

                DictionaryTableAdapter<T> adapter = new DictionaryTableAdapter<T>(table);
                                
                return new BindCleanupResult
                {
                    Result = adapter,
                    Cleanup = () => adapter.Flush()
                };
            }
        }

        public ICloudTableBinder TryGetBinder(Type targetType, bool isReadOnly)
        {
            if (targetType.IsGenericType)
            {
                if (targetType.GetGenericTypeDefinition() == typeof(IDictionary<,>))
                {
                    var args = targetType.GetGenericArguments();
                    var tKey = args[0];
                    var tValue = args[1];
                    if (tKey == typeof(Tuple<string, string>))
                    {
                        var t = typeof(TableBinder<>).MakeGenericType(tValue);
                        var binder = Activator.CreateInstance(t);
                        return (ICloudTableBinder)binder;
                    }
                }
            }
            return null;
        }
    }
}
