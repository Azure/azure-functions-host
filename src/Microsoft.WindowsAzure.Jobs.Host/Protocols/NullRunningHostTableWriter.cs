using System;

namespace Microsoft.WindowsAzure.Jobs.Host.Protocols
{
    internal class NullRunningHostTableWriter : IRunningHostTableWriter
    {
        public void SignalHeartbeat(Guid hostId)
        {
        }
    }
}
