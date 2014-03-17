using System;
using Microsoft.WindowsAzure.Jobs.Host.Protocols;

namespace Microsoft.WindowsAzure.Jobs.Host.Loggers
{
    internal class PersistentQueueFunctionInstanceLogger : IFunctionInstanceLogger
    {
        private readonly IPersistentQueue<PersistentQueueMessage> _queue;

        public PersistentQueueFunctionInstanceLogger(IPersistentQueue<PersistentQueueMessage> queue)
        {
            if (queue == null)
            {
                throw new ArgumentNullException("queue");
            }

            _queue = queue;
        }

        public void LogFunctionStarted(ExecutionInstanceLogEntity logEntity)
        {
            _queue.Enqueue(new FunctionStartedMessage { LogEntity = logEntity });
        }

        public void LogFunctionCompleted(ExecutionInstanceLogEntity logEntity)
        {
            _queue.Enqueue(new FunctionCompletedMessage { LogEntity = logEntity });
        }
    }
}
