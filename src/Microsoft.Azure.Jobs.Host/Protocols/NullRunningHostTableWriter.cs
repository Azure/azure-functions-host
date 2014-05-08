using System;

namespace Microsoft.Azure.Jobs.Host.Protocols
{
    internal class NullRunningHostTableWriter : IRunningHostTableWriter
    {
        public void SignalHeartbeat(Guid hostOrInstanceId)
        {
        }
    }
}
