using System;
using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Listeners
{
    internal class QueueTriggerExecutor : ITriggerExecutor<CloudQueueMessage>
    {
        private readonly ITriggeredFunctionInstanceFactory<CloudQueueMessage> _instanceFactory;
        private readonly IFunctionExecutor _innerExecutor;

        public QueueTriggerExecutor(ITriggeredFunctionInstanceFactory<CloudQueueMessage> instanceFactory,
            IFunctionExecutor innerExecutor)
        {
            _instanceFactory = instanceFactory;
            _innerExecutor = innerExecutor;
        }

        public bool Execute(CloudQueueMessage value)
        {
            Guid? parentId = QueueCausalityManager.GetOwner(value);
            IFunctionInstance instance = _instanceFactory.Create(value, parentId);
            return _innerExecutor.Execute(instance);
        }
    }
}
