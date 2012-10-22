using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;

namespace Executor
{
    public class ExecutionQueueSettings
    {
        public CloudStorageAccount Account { get; set; }
        public string QueueName { get; set; }

        public CloudQueue GetQueue()
        {
            var queueClient = this.Account.CreateCloudQueueClient();
            var queue = queueClient.GetQueueReference(this.QueueName);
            queue.CreateIfNotExist();
            return queue;
        }
    }
}