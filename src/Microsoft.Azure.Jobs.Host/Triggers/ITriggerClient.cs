using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Listeners;

namespace Microsoft.Azure.Jobs.Host.Triggers
{
    internal interface ITriggerClient
    {
        IFunctionBinding FunctionBinding { get; }

        IListenerFactory ListenerFactory { get; }
    }
}
