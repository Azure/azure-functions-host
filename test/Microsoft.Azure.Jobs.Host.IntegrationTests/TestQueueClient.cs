using System.Diagnostics;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.IntegrationTests
{
    public static class TestQueueClient
    {
        [DebuggerNonUserCode]
        public static void DeleteQueue(CloudStorageAccount account, string queueName)
        {
            QueueClient.ValidateQueueName(queueName);

            var client = account.CreateCloudQueueClient();
            var q = client.GetQueueReference(queueName);

            DeleteQueue(q);
        }

        [DebuggerNonUserCode]
        public static void DeleteQueue(CloudQueue queue)
        {
            try
            {
                queue.Delete();
            }
            catch (StorageException)
            {
            }
        }

    }
}
