using System;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class NullProcessTerminationSignalReader : IProcessTerminationSignalReader
    {
        public bool IsTerminationRequested(Guid hostInstanceId)
        {
            return false;
        }
    }
}
