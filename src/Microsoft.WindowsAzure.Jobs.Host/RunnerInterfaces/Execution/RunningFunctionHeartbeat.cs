using System;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class RunningFunctionHeartbeat : IHeartbeat
    {
        private readonly IFunctionUpdatedLogger _logger;
        private readonly ExecutionInstanceLogEntity _logItem;
        private readonly int _invalidationInMilliseconds;

        public RunningFunctionHeartbeat(IFunctionUpdatedLogger logger, ExecutionInstanceLogEntity logItem,
            int invalidationInMilliseconds)
        {
            if (logger == null)
            {
                throw new ArgumentNullException("logger");
            }

            if (logItem == null)
            {
                throw new ArgumentNullException("logItem");
            }

            if (invalidationInMilliseconds <= 0)
            {
                throw new ArgumentOutOfRangeException("invalidationInMilliseconds");
            }

            _logger = logger;
            _logItem = logItem;
            _invalidationInMilliseconds = invalidationInMilliseconds;
        }

        public void Beat()
        {
            Signal(DateTime.UtcNow.AddMilliseconds(_invalidationInMilliseconds));
        }

        private void Signal(DateTime invalidAfterUtc)
        {
            _logItem.HeartbeatExpires = invalidAfterUtc;
            _logger.Log(_logItem);
        }
    }
}
