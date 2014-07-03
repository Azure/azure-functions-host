using System;
using System.Globalization;
using Microsoft.Azure.Jobs.Protocols;

namespace Dashboard.Indexers
{
    internal class Indexer : IIndexer
    {
        private readonly IPersistentQueueReader<PersistentQueueMessage> _queueReader;
        private readonly IHostIndexer _hostIndexer;
        private readonly IFunctionIndexer _functionIndexer;

        public Indexer(IPersistentQueueReader<PersistentQueueMessage> queueReader,
            IHostIndexer hostIndexer,
            IFunctionIndexer functionIndexer)
        {
            _queueReader = queueReader;
            _hostIndexer = hostIndexer;
            _functionIndexer = functionIndexer;
        }

        public void Update()
        {
            PersistentQueueMessage message = _queueReader.Dequeue();

            while (message != null)
            {
                Process(message);
                _queueReader.Delete(message);

                message = _queueReader.Dequeue();
            }
        }

        private void Process(PersistentQueueMessage message)
        {
            HostStartedMessage hostStartedMessage = message as HostStartedMessage;

            if (hostStartedMessage != null)
            {
                _hostIndexer.ProcessHostStarted(hostStartedMessage);
                return;
            }

            FunctionCompletedMessage functionCompletedMessage = message as FunctionCompletedMessage;

            if (functionCompletedMessage != null)
            {
                _functionIndexer.ProcessFunctionCompleted(functionCompletedMessage);
                return;
            }

            FunctionStartedMessage functionStartedMessage = message as FunctionStartedMessage;

            if (functionStartedMessage != null)
            {
                _functionIndexer.ProcessFunctionStarted(functionStartedMessage);
                return;
            }

            string errorMessage =
                String.Format(CultureInfo.InvariantCulture, "Unknown message type '{0}'.", message.Type);
            throw new InvalidOperationException(errorMessage);
        }
    }
}
