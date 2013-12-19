using System;
using System.Threading;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    // Enqueue an azure queue message to notify that we have a new blob. 
    // ### This overlaps Services.GetBlobWrittenQueue()
    internal class NotifyNewBlobViaQueueMessage : INotifyNewBlob, INotifyNewBlobListener
    {
        // ### Put this name somewhere common
        public const string BlobWrittenQueue = "blob-written";

        private readonly CloudStorageAccount _account;

        public NotifyNewBlobViaQueueMessage(CloudStorageAccount account)
        {
            _account = account;
        }

        public void Notify(BlobWrittenMessage msg)
        {
            var queue = GetQueue();
            var q = new QueueBinder<BlobWrittenMessage>(queue);
            q.Add(msg);
        }

        // Get the queue that we read / write to. 
        // Exposes so that a listener can pull the messages out. 
        // Messages are just JSON serialized BlobWrittenMessage 
        private CloudQueue GetQueue()
        {
            CloudQueueClient client = _account.CreateCloudQueueClient();
            var queue = client.GetQueueReference(BlobWrittenQueue);
            return queue;
        }

        public void ProcessMessages(Action<BlobWrittenMessage> fpOnNewBlob, CancellationToken token)
        {
            QueueClient.ApplyToQueue<BlobWrittenMessage>(fpOnNewBlob, GetQueue());
        }
    }
}
