using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    // Full permission to a queue
    internal class CloudQueueDescriptor
    {
        public string AccountConnectionString { get; set; }

        public string QueueName { get; set; }

        public CloudStorageAccount GetAccount()
        {
            return Utility.GetAccount(AccountConnectionString);
        }

        public CloudQueue GetQueue()
        {
            var q = GetAccount().CreateCloudQueueClient().GetQueueReference(QueueName);
            q.CreateIfNotExist();
            return q;
        }

        public string GetId()
        {
            string accountName = GetAccount().Credentials.AccountName;
            return string.Format(@"{0}\{1}", accountName, QueueName);
        }

        public override string ToString()
        {
            return GetId();
        }
    }
}
