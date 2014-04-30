using System;

namespace Microsoft.Azure.Jobs.Host.Protocols
{
    internal interface IProcessTerminationSignalReader
    {
        bool IsTerminationRequested(Guid hostInstanceId);
    }
}
