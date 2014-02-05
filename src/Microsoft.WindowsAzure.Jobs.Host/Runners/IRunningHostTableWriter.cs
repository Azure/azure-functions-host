using System;

namespace Microsoft.WindowsAzure.Jobs
{
    internal interface IRunningHostTableWriter
    {
        void SignalHeartbeat(Guid hostId);
    }
}
