using System;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Loggers
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

        public void LogFunctionStarted(FunctionStartedSnapshot snapshot)
        {
            _queue.Enqueue(new FunctionStartedMessage { Snapshot = snapshot });
        }

        public void LogFunctionCompleted(FunctionCompletedSnapshot snapshot)
        {
            _queue.Enqueue(new FunctionCompletedMessage { Snapshot = snapshot });
        }
    }
}
