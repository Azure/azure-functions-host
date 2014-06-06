using System;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Triggers
{
    internal class PollQueueCommand : ICanFailCommand
    {
        private readonly CloudQueue _queue;
        private readonly QueueTrigger _trigger;
        private readonly ITriggerInvoke _invoker;
        private readonly RuntimeBindingProviderContext _context;

        public PollQueueCommand(CloudQueue queue, QueueTrigger trigger, ITriggerInvoke invoker, RuntimeBindingProviderContext context)
        {
            _queue = queue;
            _trigger = trigger;
            _invoker = invoker;
            _context = context;
        }

        public bool TryExecute()
        {
            if (_context.CancellationToken.IsCancellationRequested)
            {
                return true;
            }

            if (!_queue.Exists())
            {
                return true;
            }

            // What if job takes longer. Call CloudQueue.UpdateMessage
            var visibilityTimeout = TimeSpan.FromMinutes(10); // long enough to process the job
            CloudQueueMessage message;

            try
            {
                message = _queue.GetMessage(visibilityTimeout);
            }
            catch (StorageException)
            {
                // TODO: Consider a more specific check here, like:
                //if (exception.IsNotFound() || exception.IsServerSideError()) { return false; } else { throw; }

                // Storage exceptions can happen naturally and intermittently from network connectivity issues.
                // Just ignore.
                return false;
            }

            if (message != null)
            {
                using (IntervalSeparationTimer timer = CreateUpdateMessageVisibilityTimer(_queue, message, visibilityTimeout))
                {
                    timer.Start(executeFirst: false);

                    if (_context.CancellationToken.IsCancellationRequested)
                    {
                        return true;
                    }

                    _invoker.OnNewQueueItem(message, _trigger, _context);
                }

                try
                {
                    // Need to call Delete message only if function succeeded.
                    _queue.DeleteMessage(message);
                }
                catch (StorageException)
                {
                    // TODO: Consider a more specific check here.
                    return false;
                }
            }

            return true;
        }

        private static IntervalSeparationTimer CreateUpdateMessageVisibilityTimer(CloudQueue queue,
            CloudQueueMessage message, TimeSpan visibilityTimeout)
        {
            // Update a message's visibility when it is halfway to expiring.
            TimeSpan normalUpdateInterval = new TimeSpan(visibilityTimeout.Ticks / 2);

            ICanFailCommand command = new UpdateQueueMessageVisibilityCommand(queue, message, visibilityTimeout);
            return LinearSpeedupTimerCommand.CreateTimer(command, normalUpdateInterval, TimeSpan.FromMinutes(1));
        }
    }
}
