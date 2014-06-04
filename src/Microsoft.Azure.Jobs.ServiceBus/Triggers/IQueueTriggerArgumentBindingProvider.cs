using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Triggers
{
    internal interface IQueueTriggerArgumentBindingProvider
    {
        IArgumentBinding<BrokeredMessage> TryCreate(ParameterInfo parameter);
    }
}
