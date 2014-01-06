using System;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class RunningHostHeartbeat : IHeartbeat
    {
        private readonly IRunningHostTableWriter _heartbeatTable;
        private readonly string _hostName;

        public RunningHostHeartbeat(IRunningHostTableWriter heartbeatTable, string hostName)
        {
            if (heartbeatTable == null)
            {
                throw new ArgumentNullException("heartbeatTable");
            }

            if (hostName == null)
            {
                throw new ArgumentNullException("hostName");
            }

            _heartbeatTable = heartbeatTable;
            _hostName = hostName;
        }

        public void Beat()
        {
            _heartbeatTable.SignalHeartbeat(_hostName);
        }
    }
}
