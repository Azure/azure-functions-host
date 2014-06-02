using Microsoft.Azure.Jobs.Host.Storage;
using Microsoft.Azure.Jobs.Host.Storage.Table;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs
{
    // Allow hooking the azure table creation for unit testing.
    // Lets tests use in-memory tables instead of live azure storage.
    // $$$ Make this private/internal, go through an extensibility model?
    internal class TableProviderTestHook
    {
        public virtual ICloudTableClient Create(string accountConnectionString)
        {
            ICloudStorageAccount account = new SdkCloudStorageAccount(CloudStorageAccount.Parse(accountConnectionString));
            return account.CreateCloudTableClient();
        }

        // Tests can override
        public static TableProviderTestHook Default = new TableProviderTestHook();
    }
}
