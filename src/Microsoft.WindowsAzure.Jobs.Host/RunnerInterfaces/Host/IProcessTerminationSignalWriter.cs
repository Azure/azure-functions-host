using System;

namespace Microsoft.WindowsAzure.Jobs
{
    internal interface IProcessTerminationSignalWriter
    {
        void RequestTermination(Guid hostInstanceId);
    }
}
