using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Bindings
{
    internal class CloudQueueMessageArgumentBindingProvider : IQueueArgumentBindingProvider
    {
        public IArgumentBinding<CloudQueue> TryCreate(ParameterInfo parameter)
        {
            if (!parameter.IsOut || parameter.ParameterType != typeof(string).MakeByRefType())
            {
                return null;
            }

            return new CloudQueueMessageArgumentBinding();
        }
    }
}
