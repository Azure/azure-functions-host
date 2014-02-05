using System;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class NullRunningHostTableWriter : IRunningHostTableWriter
    {
        public void SignalHeartbeat(Guid hostId)
        {
        }
    }
}
