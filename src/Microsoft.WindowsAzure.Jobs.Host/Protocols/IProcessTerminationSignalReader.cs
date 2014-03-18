using System;

namespace Microsoft.WindowsAzure.Jobs.Host.Protocols
{
    internal interface IProcessTerminationSignalReader
    {
        bool IsTerminationRequested(Guid hostInstanceId);
    }
}
