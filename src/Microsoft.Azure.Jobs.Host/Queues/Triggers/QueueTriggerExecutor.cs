using System;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Triggers
{
    internal class QueueTriggerExecutor : ITriggerExecutor<CloudQueueMessage>
    {
        private readonly IFunctionExecutor _innerExecutor;
        private readonly ITriggeredFunctionBinding<CloudQueueMessage> _functionBinding;
        private readonly RuntimeBindingProviderContext _context;
        private readonly FunctionDescriptor _functionDescriptor;
        private readonly MethodInfo _method;

        public QueueTriggerExecutor(IFunctionExecutor innerExecutor,
            ITriggeredFunctionBinding<CloudQueueMessage> functionBinding, RuntimeBindingProviderContext context,
            FunctionDescriptor functionDescriptor, MethodInfo method)
        {
            _innerExecutor = innerExecutor;
            _functionBinding = functionBinding;
            _context = context;
            _functionDescriptor = functionDescriptor;
            _method = method;
        }

        public bool Execute(CloudQueueMessage value)
        {
            Guid functionInstanceId = Guid.NewGuid();
            IBindCommand bindCommand = new TriggerBindCommand<CloudQueueMessage>(_functionBinding, _context, functionInstanceId, value);
            Guid? parentId = new QueueCausalityHelper().GetOwner(value);
            IFunctionInstance instance = new FunctionInstance(functionInstanceId, parentId,
                ExecutionReason.AutomaticTrigger, bindCommand, _functionDescriptor, _method);
            return _innerExecutor.Execute(instance);
        }
    }
}
