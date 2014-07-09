using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal interface IWatcher
    {
        ParameterLog GetStatus();
    }
}
