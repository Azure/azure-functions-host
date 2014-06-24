using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Indexers
{
    internal interface IFunctionDefinition
    {
        FunctionDescriptor Descriptor { get; }

        IFunctionBinding Binding { get; }

        IListenerFactory ListenerFactory { get; }
    }
}
