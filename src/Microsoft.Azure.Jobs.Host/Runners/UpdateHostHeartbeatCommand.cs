using System;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs
{
    internal class UpdateHostHeartbeatCommand : ICanFailCommand
    {
        private readonly IRunningHostTableWriter _heartbeatTable;
        private readonly Guid _hostId;

        public UpdateHostHeartbeatCommand(IRunningHostTableWriter heartbeatTable, Guid hostId)
        {
            if (heartbeatTable == null)
            {
                throw new ArgumentNullException("heartbeatTable");
            }

            _heartbeatTable = heartbeatTable;
            _hostId = hostId;
        }

        public bool TryExecute()
        {
            try
            {
                _heartbeatTable.SignalHeartbeat(_hostId);
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
