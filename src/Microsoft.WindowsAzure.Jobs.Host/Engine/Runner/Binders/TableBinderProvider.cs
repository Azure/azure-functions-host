using System;
using AzureTables;

namespace Microsoft.WindowsAzure.Jobs
{
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
}
