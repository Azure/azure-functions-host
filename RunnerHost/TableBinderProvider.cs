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
    class QueueOutputProvider : ICloudBinderProvider
    {
        class QueueOutputBinder : ICloudBinder
        {
            public Type queueType;
            public BindResult Bind(IBinderEx bindingContext, ParameterInfo parameter)
            {
                CloudStorageAccount account = Utility.GetAccount(bindingContext.AccountConnectionString);

                // How to get q-name? Or other info from the attributes.
                string queueName = parameter.Name;

                var q = account.CreateCloudQueueClient().GetQueueReference(queueName);

                var obj = Activator.CreateInstance(queueType, new object[] { q });
                return new BindResult { Result = obj } ;
            }
        }

        public ICloudBinder TryGetBinder(Type targetType)
        {
            if (targetType.IsGenericType)
            {
                if (targetType.GetGenericTypeDefinition() == typeof(IQueueOutput<>))
                {
                    var args = targetType.GetGenericArguments();
                    var t2 = typeof(QueueBinder<>).MakeGenericType(args[0]);

                    return new QueueOutputBinder { queueType = t2 };
                }
            }
            return null;
        }
    }


    class TableBinderProvider : ICloudTableBinderProvider
    {
        static TableBinder _singleton = new TableBinder();

        class TableBinder : ICloudTableBinder
        {
            public BindResult Bind(IBinderEx bindingContext, Type targetType, string tableName)
            {
                CloudStorageAccount account = Utility.GetAccount(bindingContext.AccountConnectionString);

                // Read / write access is determined by the interface
                // $$$ So caller could always just cast. We could make that more robust.
                AzureTable table = new AzureTable(account, tableName);

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
                CloudStorageAccount account = Utility.GetAccount(bindingContext.AccountConnectionString);

                AzureTable<T> table = new AzureTable<T>(account, tableName);
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
        class TableBinder : ICloudTableBinder
        {
            public Type azureTableType;
            public BindResult Bind(IBinderEx bindingContext, Type targetType, string tableName)
            {
                CloudStorageAccount account = Utility.GetAccount(bindingContext.AccountConnectionString);

                var obj = Activator.CreateInstance(azureTableType, account, tableName);
                IAzureTableWriter writer = (IAzureTableWriter) obj;
                return new BindCleanupResult 
                { 
                    Result = obj,
                    Cleanup = () => writer.Flush()
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
                    var t2 = typeof(AzureTable<>).MakeGenericType(args[0]);
                    return new TableBinder { azureTableType = t2 };

                }
            }
            return null;
        }
    }

}