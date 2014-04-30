using System;

namespace Microsoft.Azure.Jobs.Host.Protocols
{
    internal class NullProcessTerminationSignalReader : IProcessTerminationSignalReader
    {
        public bool IsTerminationRequested(Guid hostInstanceId)
        {
            return false;
        }
    }
}
