using System;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Listeners
{
    internal sealed class PollQueueCommand : IIntervalSeparationCommand
    {
        private static TimeSpan _normalSeparationInterval = TimeSpan.FromSeconds(2);

        private readonly CloudQueue _queue;
        private readonly ITriggerExecutor<CloudQueueMessage> _triggerExecutor;

        private TimeSpan _separationInterval;
        
        public PollQueueCommand(CloudQueue queue, ITriggerExecutor<CloudQueueMessage> triggerExecutor)
        {
            _queue = queue;
            _triggerExecutor = triggerExecutor;
            _separationInterval = TimeSpan.Zero; // Start polling immediately
        }

        public TimeSpan SeparationInterval
        {
            get { return _separationInterval; }
        }

        public void Execute()
        {
            // After starting up, wait two seconds after Execute returns before polling again.
            _separationInterval = _normalSeparationInterval;

            if (!_queue.Exists())
            {
                return;
            }

            // What if job takes longer. Call CloudQueue.UpdateMessage
            TimeSpan visibilityTimeout = TimeSpan.FromMinutes(10); // long enough to process the job
            CloudQueueMessage message;

            do
            {
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
                    return;
                }

                if (message != null)
                {
                    bool succeeded;

                    using (IntervalSeparationTimer timer = CreateUpdateMessageVisibilityTimer(_queue, message, visibilityTimeout))
                    {
                        timer.Start(executeFirst: false);

                        succeeded = _triggerExecutor.Execute(message);
                    }

                    // Need to call Delete message only if function succeeded.
                    if (succeeded)
                    {
                        try
                        {
                            _queue.DeleteMessage(message);
                        }
                        catch (StorageException)
                        {
                            // TODO: Consider a more specific check here.
                            return;
                        }
                    }
                }
            } while (message != null);
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
