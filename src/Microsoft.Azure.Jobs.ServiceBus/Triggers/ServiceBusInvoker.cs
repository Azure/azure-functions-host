using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Runners;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.Azure.Jobs.ServiceBus.Triggers;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs
{
    internal class ServiceBusInvoker
    {
        private readonly Worker _worker;

        public ServiceBusInvoker(Worker worker)
        {
            _worker = worker;
        }

        public static FunctionInvokeRequest GetFunctionInvocation(FunctionDefinition func, BrokeredMessage msg)
        {
            // Extract any named parameters from the queue payload.
            ServiceBusTriggerBinding serviceBusTriggerBinding = func.TriggerBinding as ServiceBusTriggerBinding;
            ITriggerData triggerData = serviceBusTriggerBinding.Bind(msg);
            IDictionary<string, string> p = Worker.GetNameParameters(triggerData.BindingData);

            // msg was the one that triggered it.
            RuntimeBindingInputs ctx = new RuntimeBindingInputs(func.Location)
            {
                NameParameters = p
            };

            var instance = Worker.BindParameters(ctx, func);

            instance.TriggerReason = new ServiceBusTriggerReason
            {
                EntityPath = serviceBusTriggerBinding.EntityPath,
                MessageId = msg.MessageId,
                ParentGuid = GetOwnerFromMessage(msg)
            };

            instance.Parameters = new Dictionary<string, IValueProvider> {
                { func.TriggerParameterName, triggerData.ValueProvider }
            };

            return instance;
        }
        private static Guid GetOwnerFromMessage(BrokeredMessage msg)
        {
            var qcm = new ServiceBusCausalityHelper();
            return qcm.GetOwner(msg);
        }

        public void OnNewServiceBusMessage(ServiceBusTrigger trigger, BrokeredMessage msg, CancellationToken cancellationToken)
        {
            var instance = GetFunctionInvocation((FunctionDefinition)trigger.Tag, msg);
            _worker.OnNewInvokeableItem(instance, cancellationToken);
        }
    }
}