using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Protocols;
using Newtonsoft.Json;

namespace Microsoft.Azure.Jobs
{
    internal class ServiceBusParameterStaticBinding : ParameterStaticBinding
    {
        // Is this enqueue or dequeue?
        public bool IsInput { get; set; }

        // Route params produced from this message. 
        // This likely corresponds to simply properties on the ServiceBus Parameter type.
        public string[] Params { get; set; }


        public string EntityPath { get; set; }

        public override IEnumerable<string> ProducedRouteParameters
        {
            get
            {
                return Params ?? new string[0];
            }
        }

        public override ParameterRuntimeBinding Bind(IRuntimeBindingInputs inputs)
        {
            if (this.IsInput)
            {
                ITriggerNewServiceBusQueueMessage trigger = inputs as ITriggerNewServiceBusQueueMessage;

                if (trigger == null)
                {
                    throw new InvalidOperationException("Direct calls are not supported for ServiceBus methods.");
                }

                return new ServiceBusInputParameterRuntimeBinding { Name = Name, Message = trigger.InputMessage};
            }
            return new ServiceBusOutputParameterRuntimeBinding
            {
                Name = Name,
                EntityPath = EntityPath,
                ServiceBusConnectionString = inputs.ServiceBusConnectionString
            };
        }

        public override ParameterRuntimeBinding BindFromInvokeString(IRuntimeBindingInputs inputs, string invokeString)
        {
            throw new NotImplementedException("Service Bus invocations from string is not yet implemented.");
        }

        public override ParameterDescriptor ToParameterDescriptor()
        {
            return new ServiceBusParameterDescriptor
            {
                EntityPath = EntityPath,
                IsInput = IsInput
            };
        }
    }
}