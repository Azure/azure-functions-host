using System;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Bindings
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
                if (!IsNotFound(exception))
                {
                    throw;
                }

                try
                {
                    queue.CreateIfNotExists();
                }
                catch (StorageException creationException)
                {
                    if (!IsNotFound(creationException))
                    {
                        throw;
                    }
                }

                queue.AddMessage(message);
            }
        }

        private static bool IsNotFound(StorageException exception)
        {
            RequestResult result = exception.RequestInformation;

            if (result == null)
            {
                return false;
            }

            return result.HttpStatusCode == 404;
        }
    }
}
