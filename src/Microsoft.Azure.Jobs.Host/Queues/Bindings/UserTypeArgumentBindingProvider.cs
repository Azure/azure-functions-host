using System;
using System.Collections;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Bindings
{
    internal class UserTypeArgumentBindingProvider : IQueueArgumentBindingProvider
    {
        public IArgumentBinding<CloudQueue> TryCreate(ParameterInfo parameter)
        {
            if (!parameter.IsOut)
            {
                return null;
            }

            Type parameterType = parameter.ParameterType.GetElementType();

            if (typeof(IEnumerable).IsAssignableFrom(parameterType))
            {
                throw new InvalidOperationException("Non-collection enumerable types are not supported.");
            }
            else if (typeof(object) == parameterType)
            {
                throw new InvalidOperationException("Object element types are not supported.");
            }

            return new UserTypeArgumentBinding(parameterType);
        }
    }
}
