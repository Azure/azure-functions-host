using System;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class UpdateHostHeartbeatCommand : ICanFailCommand
    {
        private readonly IRunningHostTableWriter _heartbeatTable;
        private readonly string _hostName;

        public UpdateHostHeartbeatCommand(IRunningHostTableWriter heartbeatTable, string hostName)
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

        public bool TryExecute()
        {
            try
            {
                _heartbeatTable.SignalHeartbeat(_hostName);
                return true;
            }
            catch (StorageClientException exception)
            {
                if (exception.IsServerSideError())
                {
                    return false;
                }

                throw;
            }
        }
    }
}
