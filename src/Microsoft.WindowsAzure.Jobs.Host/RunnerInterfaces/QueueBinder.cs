using System;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class QueueBinder<T> : IQueueOutput<T>, ISelfWatch
    {
        private readonly CloudQueue _queue;

        public QueueBinder(CloudQueue queue)
        {
            if (queue == null)
            {
                throw new ArgumentNullException("queue");
            }
            _queue = queue;            
        }

        public void Add(T payload)
        {
            _count++;

            string json = JsonCustom.SerializeObject(payload);
            CloudQueueMessage msg = new CloudQueueMessage(json);
            _queue.CreateIfNotExist();
            _queue.AddMessage(msg);
        }

        public void Add(T payload, TimeSpan delay)
        {
            _count++;

            string json = JsonCustom.SerializeObject(payload);
            CloudQueueMessage msg = new CloudQueueMessage(json);
            _queue.CreateIfNotExist();
            _queue.AddMessage(msg, null, delay);
        }

        volatile int _count;

        public string GetStatus()
        {
            return string.Format("Queued {0} messages", _count);
        }
    }
}
