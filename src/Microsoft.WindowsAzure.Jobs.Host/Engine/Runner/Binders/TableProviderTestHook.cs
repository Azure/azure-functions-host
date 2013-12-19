using AzureTables;

namespace Microsoft.WindowsAzure.Jobs
{
    // Allow hooking the azure table creation for unit testing.
    // Lets tests use in-memory tables instead of live azure storage.
    // $$$ Make this private/internal, go through an extensibility model?
    internal class TableProviderTestHook
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
}
