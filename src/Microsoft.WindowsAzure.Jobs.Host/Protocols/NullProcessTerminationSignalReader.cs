using System;

namespace Microsoft.WindowsAzure.Jobs.Host.Protocols
{
    internal class NullProcessTerminationSignalReader : IProcessTerminationSignalReader
    {
        public bool IsTerminationRequested(Guid hostInstanceId)
        {
            return false;
        }
    }
}
