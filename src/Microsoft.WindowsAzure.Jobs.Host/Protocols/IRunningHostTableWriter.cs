using System;

namespace Microsoft.WindowsAzure.Jobs.Host.Protocols
{
    internal interface IRunningHostTableWriter
    {
        void SignalHeartbeat(Guid hostId);
    }
}
