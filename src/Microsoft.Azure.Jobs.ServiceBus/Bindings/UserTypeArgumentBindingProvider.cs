using System;
using System.Collections;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.ServiceBus.Bindings
{
    internal class UserTypeArgumentBindingProvider : IServiceBusArgumentBindingProvider
    {
        public IArgumentBinding<ServiceBusEntity> TryCreate(ParameterInfo parameter)
        {
            if (!parameter.IsOut)
            {
                return null;
            }

            Type parameterType = parameter.ParameterType;

            if (typeof(IEnumerable).IsAssignableFrom(parameterType))
            {
                throw new InvalidOperationException("Non-collection enumerable types are not supported.");
            }

            return new UserTypeArgumentBinding(parameterType);
        }
    }
}
