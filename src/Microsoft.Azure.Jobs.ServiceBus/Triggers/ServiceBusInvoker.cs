using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
            var flow = func.Flow;
            ServiceBusParameterStaticBinding qb = flow.Bindings.OfType<ServiceBusParameterStaticBinding>().Where(b => b.IsInput).First();

            // msg was the one that triggered it.
            IDictionary<string, string> p;
            try
            {
                var payload = new StreamReader(msg.Clone().GetBody<Stream>()).ReadToEnd();
                p = QueueInputParameterRuntimeBinding.GetRouteParameters(payload, qb.Params);
            }
            catch
            {
                p = null;
            }
            RuntimeBindingInputs ctx = new NewServiceBusMessageRuntimeBindingInputs(func.Location, msg)
            {
                NameParameters = p
            };

            var instance = Worker.BindParameters(ctx, func);

            instance.TriggerReason = new ServiceBusTriggerReason
            {
                EntityPath = qb.EntityPath,
                MessageId = msg.MessageId,
                ParentGuid = GetOwnerFromMessage(msg)
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