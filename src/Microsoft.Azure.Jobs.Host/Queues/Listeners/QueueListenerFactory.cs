using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Listeners
{
    internal class QueueListenerFactory : IListenerFactory
    {
        private readonly CloudQueue _queue;
        private readonly ITriggeredFunctionBinding<CloudQueueMessage> _functionBinding;
        private readonly FunctionDescriptor _functionDescriptor;
        private readonly MethodInfo _method;

        public QueueListenerFactory(CloudQueue queue, ITriggeredFunctionBinding<CloudQueueMessage> functionBinding,
            FunctionDescriptor functionDescriptor, MethodInfo method)
        {
            _queue = queue;
            _functionBinding = functionBinding;
            _functionDescriptor = functionDescriptor;
            _method = method;
        }

        public IListener Create(IFunctionExecutor executor, RuntimeBindingProviderContext context)
        {
            QueueTriggerExecutor triggerExecutor = new QueueTriggerExecutor(executor, _functionBinding, context,
                _functionDescriptor, _method);
            PollQueueCommand command = new PollQueueCommand(_queue, triggerExecutor);
            IntervalSeparationTimer timer = new IntervalSeparationTimer(command);
            return new TimerListener(timer);
        }
    }
}
