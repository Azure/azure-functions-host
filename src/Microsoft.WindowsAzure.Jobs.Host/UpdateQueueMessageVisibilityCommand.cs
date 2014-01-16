using System;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class UpdateQueueMessageVisibilityCommand : ICanFailCommand
    {
        private readonly CloudQueue _queue;
        private readonly CloudQueueMessage _message;
        private readonly TimeSpan _visibilityTimeout;

        public UpdateQueueMessageVisibilityCommand(CloudQueue queue, CloudQueueMessage message, TimeSpan visibilityTimeout)
        {
            if (queue == null)
            {
                throw new ArgumentNullException("queue");
            }

            if (message == null)
            {
                throw new ArgumentNullException("message");
            }

            _queue = queue;
            _message = message;
            _visibilityTimeout = visibilityTimeout;
        }

        public bool TryExecute()
        {
            try
            {
                _queue.UpdateMessage(_message, _visibilityTimeout, MessageUpdateFields.Visibility);
                return true;
            }
            catch (StorageClientException exception)
            {
                if (exception.IsServerSideError())
                {
                    return false;
                }

                throw;
            }
        }
    }
}
