using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.ServiceBus.Bindings
{
    internal class StringArgumentBindingProvider : IServiceBusArgumentBindingProvider
    {
        public IArgumentBinding<ServiceBusEntity> TryCreate(ParameterInfo parameter)
        {
            if (!parameter.IsOut || parameter.ParameterType != typeof(string).MakeByRefType())
            {
                return null;
            }

            return new StringArgumentBinding();
        }
    }
}
