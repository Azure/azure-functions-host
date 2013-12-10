using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;

namespace RunnerInterfaces
{
    // $$$ ISn't th
    // $$$ USe CloudBlobDescriptor? AccountName vs. AccountConnectionString
    public class BlobWrittenMessage
    {
        public string AccountName { get; set; }
        public string ContainerName { get; set; }
        public string BlobName { get; set; }
    }

    // Listening to external blobs is slow. But when we write a blob ourselves, we can hook the notifations
    // so that we detect the new blob immediately without polling.
    public interface INotifyNewBlob
    {
        void Notify(BlobWrittenMessage msg);
    }

    public static class INotifyNewBlobExtensions
    {
        public static void Notify(this INotifyNewBlob x, string accountName, string containerName, string blobName)
        {
            BlobWrittenMessage msg = new BlobWrittenMessage
            {
                AccountName = accountName,
                BlobName = blobName,
                ContainerName = containerName
            };
            x.Notify(msg);
        }
    }

    // Listen on new blobs, invoke a callback when they're detected.
    // This is a fast-path form of blob listening. 
    // ### Can this be merged with the other general blob listener or IBlobListener?     
    public interface INotifyNewBlobListener
    {
        void ProcessMessages(Action<BlobWrittenMessage> fpOnNewBlob, CancellationToken token);
    }

    // Enqueue an azure queue message to notify that we have a new blob. 
    // This is useful when everything is in the same process. It avoids an Azure queue that     
    // could be conflicting between multiple users sharing the same logging account. 
    public class NotifyNewBlobViaInMemory : INotifyNewBlob, INotifyNewBlobListener
    {
        static ConcurrentQueue<BlobWrittenMessage> _queue = new ConcurrentQueue<BlobWrittenMessage>();

        public NotifyNewBlobViaInMemory()
        {            
        }

        public void Notify(BlobWrittenMessage msg)
        {
            _queue.Enqueue(msg);
        }        

        public void ProcessMessages(Action<BlobWrittenMessage> fpOnNewBlob, CancellationToken token)
        {
            BlobWrittenMessage msg;
            while (!token.IsCancellationRequested && _queue.TryDequeue(out msg))
            {
                fpOnNewBlob(msg);
            }
        }
    }

    // Enqueue an azure queue message to notify that we have a new blob. 
    // ### This overlaps Services.GetBlobWrittenQueue()
    public class NotifyNewBlobViaQueueMessage : INotifyNewBlob, INotifyNewBlobListener
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
            Utility.ApplyToQueue<BlobWrittenMessage>(fpOnNewBlob, GetQueue());
        }
    }

    // Ping a webservice to notify that there's a new blob. 
    public class NotifyNewBlobViaWebApi : INotifyNewBlob
    {
        private readonly string _serviceUrl;

        public NotifyNewBlobViaWebApi(string serviceUrl)
        {
            this._serviceUrl = serviceUrl;
        }

        public void Notify(BlobWrittenMessage msg)
        {
            // $$$ What about escaping and encoding?
            StringBuilder sb = new StringBuilder();
            sb.Append(_serviceUrl);
            sb.Append("/api/execution/NotifyBlob");

            try
            {
                Utility.PostJson(sb.ToString(), msg);
            }
            catch
            {
                // Ignorable. 
            }
        }
    }
}