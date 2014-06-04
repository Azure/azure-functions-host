using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Triggers
{
    internal interface IQueueTriggerArgumentBindingProvider
    {
        IArgumentBinding<CloudQueueMessage> TryCreate(ParameterInfo parameter);
    }
}
