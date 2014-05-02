using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs
{
    internal interface ITriggerNewServiceBusQueueMessage : IRuntimeBindingInputs
    {
        // If null, then ignore. 
        BrokeredMessage InputMessage { get; }
    }
}