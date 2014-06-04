using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Bindings
{
    internal class BrokeredMessageArgumentBindingProvider : IQueueArgumentBindingProvider
    {
        public IArgumentBinding<ServiceBusEntity> TryCreate(ParameterInfo parameter)
        {
            if (!parameter.IsOut || parameter.ParameterType != typeof(BrokeredMessage).MakeByRefType())
            {
                return null;
            }

            return new BrokeredMessageArgumentBinding();
        }
    }
}
