using System;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs
{
    internal class ServiceBusParameterStaticBinding : ParameterStaticBinding
    {
        public string EntityPath { get; set; }

        public override ParameterRuntimeBinding Bind(IRuntimeBindingInputs inputs)
        {
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
                IsInput = false
            };
        }
    }
}