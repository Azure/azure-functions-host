using System;

namespace Microsoft.WindowsAzure.Jobs
{
    internal interface IProcessTerminationSignalReader
    {
        bool IsTerminationRequested(Guid hostInstanceId);
    }
}
