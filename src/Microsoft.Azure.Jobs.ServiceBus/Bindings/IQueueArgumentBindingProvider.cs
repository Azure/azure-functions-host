using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.ServiceBus.Bindings
{
    internal interface IQueueArgumentBindingProvider
    {
        IArgumentBinding<ServiceBusEntity> TryCreate(ParameterInfo parameter);
    }
}
