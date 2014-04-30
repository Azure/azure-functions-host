using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs
{
    // Full permission to a Table
    // This can be serialized.
    internal class CloudTableDescriptor
    {
        public string AccountConnectionString { get; set; }

        public string TableName { get; set; }

        public CloudStorageAccount GetAccount()
        {
            return Utility.GetAccount(AccountConnectionString);
        }
    }
}
