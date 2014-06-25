using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Listeners
{
    internal class QueueListenerFactory : IListenerFactory
    {
        private readonly CloudQueue _queue;
        private readonly ITriggeredFunctionInstanceFactory<CloudQueueMessage> _instanceFactory;

        public QueueListenerFactory(CloudQueue queue,
            ITriggeredFunctionInstanceFactory<CloudQueueMessage> instanceFactory)
        {
            _queue = queue;
            _instanceFactory = instanceFactory;
        }

        public IListener Create(IFunctionExecutor executor)
        {
            QueueTriggerExecutor triggerExecutor = new QueueTriggerExecutor(_instanceFactory, executor);
            PollQueueCommand command = new PollQueueCommand(_queue, triggerExecutor);
            IntervalSeparationTimer timer = new IntervalSeparationTimer(command);
            return new TimerListener(timer);
        }
    }
}
