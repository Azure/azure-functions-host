using System;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class UpdateFunctionHeartbeatCommand : ICanFailCommand
    {
        private readonly IFunctionUpdatedLogger _logger;
        private readonly ExecutionInstanceLogEntity _logItem;
        private readonly TimeSpan _invalidAfterInterval;

        public UpdateFunctionHeartbeatCommand(IFunctionUpdatedLogger logger, ExecutionInstanceLogEntity logItem,
            TimeSpan invalidAfterInterval)
        {
            if (logger == null)
            {
                throw new ArgumentNullException("logger");
            }

            if (logItem == null)
            {
                throw new ArgumentNullException("logItem");
            }

            _logger = logger;
            _logItem = logItem;
            _invalidAfterInterval = invalidAfterInterval;
        }

        public bool TryExecute()
        {
            return TryUpdateHeartbeat(DateTime.UtcNow.Add(_invalidAfterInterval));
        }

        private bool TryUpdateHeartbeat(DateTime invalidAfterUtc)
        {
            _logItem.HeartbeatExpires = invalidAfterUtc;

            try
            {
                _logger.Log(_logItem);
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
