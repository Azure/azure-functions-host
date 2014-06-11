using System;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Loggers
{
    internal class PersistentQueueFunctionInstanceLogger : IFunctionInstanceLogger
    {
        private readonly IPersistentQueueWriter<PersistentQueueMessage> _queue;

        public PersistentQueueFunctionInstanceLogger(IPersistentQueueWriter<PersistentQueueMessage> queue)
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
