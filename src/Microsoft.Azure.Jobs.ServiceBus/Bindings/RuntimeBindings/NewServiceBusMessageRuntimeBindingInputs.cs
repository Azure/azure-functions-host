using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs
{
    internal class NewServiceBusMessageRuntimeBindingInputs : RuntimeBindingInputs, ITriggerNewServiceBusQueueMessage
    {
        public NewServiceBusMessageRuntimeBindingInputs(FunctionLocation location, BrokeredMessage inputMessage)
            : base(location)
        {
            this.InputMessage = inputMessage;
        }

        // Non-null if this was triggered by a new Service Bus Q message. 
        public BrokeredMessage InputMessage { get; private set; }
    }
}