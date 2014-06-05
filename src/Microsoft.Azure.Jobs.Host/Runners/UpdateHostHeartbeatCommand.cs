using System;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs
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
            catch (StorageException exception)
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
