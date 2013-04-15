using System;
using System.Collections.Generic;
using System.Reflection;
using AzureTables;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;
using SimpleBatch;

namespace RunnerHost
{
    // Allow hooking the azure table creation for unit testing.
    // Lets tests use in-memory tables instead of live azure storage.
    // $$$ Make this private/internal, go through an extensibility model?
    public class TableProviderTestHook
    {
        public virtual AzureTable Create(string accountConnectionString, string tableName)
        {
            CloudStorageAccount account = Utility.GetAccount(accountConnectionString);
            AzureTable table = new AzureTable(account, tableName);
            return table;
        }

        public virtual AzureTable<T> Create<T>(string accountConnectionString, string tableName)
            where T : new()
        {
            CloudStorageAccount account = Utility.GetAccount(accountConnectionString);
            AzureTable<T> table = new AzureTable<T>(account, tableName);
            return table;
        }

        // Tests can override
        public static TableProviderTestHook Default = new TableProviderTestHook();
    }

    class TableBinderProvider : ICloudTableBinderProvider
    {
        static TableBinder _singleton = new TableBinder();

        class TableBinder : ICloudTableBinder
        {
            public BindResult Bind(IBinderEx bindingContext, Type targetType, string tableName)
            {                
                // Read / write access is determined by the interface
                // $$$ So caller could always just cast. We could make that more robust.
                string accountConnectionString = bindingContext.AccountConnectionString;
                AzureTable table = TableProviderTestHook.Default.Create(accountConnectionString, tableName);

                return new BindCleanupResult
                {
                    Result = table,
                    Cleanup = () => table.Flush()
                };        
            }
        }

        public ICloudTableBinder TryGetBinder(Type targetType, bool isReadOnly)
        {
            if ((targetType == typeof(IAzureTableReader)) ||
                (targetType == typeof(IAzureTableWriter)) ||
                (targetType == typeof(IAzureTable)))
            {
                return _singleton;
            }
            return null;
        }
    }

    // Bind IDictionary<Tuple<string,string>, T> to an azure table.
    class DictionaryTableBinderProvider : ICloudTableBinderProvider
    {
        class TableBinder<T> : ICloudTableBinder where T : new()
        {
            public BindResult Bind(IBinderEx bindingContext, Type targetType, string tableName)
            {
                string accountConnectionString = bindingContext.AccountConnectionString;
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