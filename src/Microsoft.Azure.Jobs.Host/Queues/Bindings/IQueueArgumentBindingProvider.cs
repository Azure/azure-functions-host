using System;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Bindings
{
    internal interface IQueueArgumentBindingProvider
    {
        IArgumentBinding<CloudQueue> TryCreate(ParameterInfo parameter);
    }
}
