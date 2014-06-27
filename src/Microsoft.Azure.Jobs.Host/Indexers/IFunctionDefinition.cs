using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Listeners;

namespace Microsoft.Azure.Jobs.Host.Indexers
{
    internal interface IFunctionDefinition
    {
        IFunctionInstanceFactory InstanceFactory { get; }

        IListenerFactory ListenerFactory { get; }
    }
}
