using System;
using Microsoft.Azure.Jobs.Host.Bindings;
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
            var instance = GetFunctionInvocation((FunctionDefinition)trigger.Tag, context, msg);
            _worker.OnNewInvokeableItem(instance, context);
        }

        private static FunctionInvokeRequest GetFunctionInvocation(FunctionDefinition func,
            RuntimeBindingProviderContext context, BrokeredMessage msg)
        {
            ServiceBusTriggerBinding serviceBusTriggerBinding = (ServiceBusTriggerBinding)func.TriggerBinding;
            Guid functionInstanceId = Guid.NewGuid();

            return new FunctionInvokeRequest
            {
                Id = functionInstanceId,
                Method = func.Method,
                ParametersProvider = new TriggerParametersProvider<BrokeredMessage>(functionInstanceId, func, msg, context),
                Reason = ExecutionReason.AutomaticTrigger,
                ParentId = ServiceBusCausalityHelper.GetOwner(msg)
            };
        }
    }
}