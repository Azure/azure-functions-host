using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Runners;
using Microsoft.Azure.Jobs.Host.Triggers;
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

        public static FunctionInvokeRequest GetFunctionInvocation(FunctionDefinition func,
            RuntimeBindingProviderContext context, BrokeredMessage msg)
        {
            Guid functionInstanceId = Guid.NewGuid();

            // Extract any named parameters from the queue payload.
            ServiceBusTriggerBinding serviceBusTriggerBinding = func.TriggerBinding as ServiceBusTriggerBinding;
            ITriggerData triggerData = serviceBusTriggerBinding.Bind(msg,
                new ArgumentBindingContext
                {
                    FunctionInstanceId = functionInstanceId,
                    NotifyNewBlob = context.NotifyNewBlob,
                    CancellationToken = context.CancellationToken,
                    ConsoleOutput = context.ConsoleOutput,
                    NameResolver = context.NameResolver,
                    StorageAccount = context.StorageAccount,
                    ServiceBusConnectionString = context.ServiceBusConnectionString,
                });

            // msg was the one that triggered it.
            var instance = Worker.CreateInvokeRequest(func, functionInstanceId);

            instance.TriggerData = triggerData;
            instance.TriggerReason = new ServiceBusTriggerReason
            {
                EntityPath = serviceBusTriggerBinding.EntityPath,
                MessageId = msg.MessageId,
                ParentGuid = GetOwnerFromMessage(msg)
            };

            return instance;
        }
        private static Guid GetOwnerFromMessage(BrokeredMessage msg)
        {
            return ServiceBusCausalityHelper.GetOwner(msg);
        }

        public void OnNewServiceBusMessage(ServiceBusTrigger trigger, BrokeredMessage msg, RuntimeBindingProviderContext context)
        {
            var instance = GetFunctionInvocation((FunctionDefinition)trigger.Tag, context, msg);
            _worker.OnNewInvokeableItem(instance, context);
        }
    }
}