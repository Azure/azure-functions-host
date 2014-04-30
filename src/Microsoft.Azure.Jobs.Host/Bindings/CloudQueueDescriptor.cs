using System;
using System.Globalization;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs
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
            q.CreateIfNotExists();
            return q;
        }

        public string GetId()
        {
            string accountName = GetAccount().Credentials.AccountName;
            return String.Format(CultureInfo.InvariantCulture, @"{0}\{1}", accountName, QueueName);
        }

        public override string ToString()
        {
            return GetId();
        }
    }
}
