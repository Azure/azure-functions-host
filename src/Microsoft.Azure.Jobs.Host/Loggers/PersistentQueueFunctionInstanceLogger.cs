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

        public void LogFunctionStarted(FunctionStartedMessage message)
        {
            _queue.Enqueue(message);
        }

        public void LogFunctionCompleted(FunctionCompletedMessage message)
        {
            _queue.Enqueue(message);
        }
    }
}
