using System;
using Microsoft.Azure.Jobs.Host.Storage;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues
{
    internal static class CloudQueueExtensions
    {
        public static void AddMessageAndCreateIfNotExists(this CloudQueue queue, CloudQueueMessage message)
        {
            if (queue == null)
            {
                throw new ArgumentNullException("queue");
            }

            try
            {
                queue.AddMessage(message);
            }
            catch (StorageException exception)
            {
                if (!exception.IsNotFoundQueueNotFound())
                {
                    throw;
                }

                queue.CreateIfNotExists();
                queue.AddMessage(message);
            }
        }
    }
}
