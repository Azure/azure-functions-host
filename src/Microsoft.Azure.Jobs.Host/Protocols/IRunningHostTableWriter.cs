using System;

namespace Microsoft.Azure.Jobs.Host.Protocols
{
    internal interface IRunningHostTableWriter
    {
        void SignalHeartbeat(Guid hostOrInstanceId);
    }
}
