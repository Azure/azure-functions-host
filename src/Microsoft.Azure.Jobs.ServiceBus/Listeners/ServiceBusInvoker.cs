using System;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Indexers;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Runners;
using Microsoft.Azure.Jobs.ServiceBus.Triggers;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Listeners
{
    internal class ServiceBusInvoker
    {
        private readonly Worker _worker;

        public ServiceBusInvoker(Worker worker)
        {
            _worker = worker;
        }

        public void OnNewServiceBusMessage(ServiceBusTrigger trigger, BrokeredMessage msg, RuntimeBindingProviderContext context)
        {
            var instance = CreateFunctionInstance((FunctionDefinition)trigger.Tag, context, msg);
            _worker.OnNewInvokeableItem(instance, context);
        }

        private static IFunctionInstance CreateFunctionInstance(FunctionDefinition func,
            RuntimeBindingProviderContext context, BrokeredMessage msg)
        {
            ServiceBusTriggerBinding serviceBusTriggerBinding = (ServiceBusTriggerBinding)func.TriggerBinding;
            Guid functionInstanceId = Guid.NewGuid();

            return new FunctionInstance(functionInstanceId,
                ServiceBusCausalityHelper.GetOwner(msg),
                ExecutionReason.AutomaticTrigger,
                new TriggerBindCommand<BrokeredMessage>(functionInstanceId, func, msg, context),
                func.Descriptor,
                func.Method);
        }
    }
}
