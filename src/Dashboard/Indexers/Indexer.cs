using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.WindowsAzure.Jobs;
using Microsoft.WindowsAzure.Jobs.Host.Protocols;

namespace Dashboard.Indexers
{
    internal class Indexer : IIndexer
    {
        private readonly IPersistentQueue<PersistentQueueMessage> _queue;
        private readonly IFunctionTable _functionTable;
        private readonly IFunctionInstanceLogger _functionInstanceLogger;

        public Indexer(IPersistentQueue<PersistentQueueMessage> queue, IFunctionTable functionTable,
            IFunctionInstanceLogger functionInstanceLogger)
        {
            _queue = queue;
            _functionTable = functionTable;
            _functionInstanceLogger = functionInstanceLogger;
        }

        public void Update()
        {
            PersistentQueueMessage message = _queue.Dequeue();

            while (message != null)
            {
                Process(message);
                _queue.Delete(message);

                message = _queue.Dequeue();
            }
        }

        private void Process(PersistentQueueMessage message)
        {
            HostStartupMessage startupMessage = message as HostStartupMessage;

            if (startupMessage != null)
            {
                Process(startupMessage);
                return;
            }

            FunctionStartedMessage functionStartedMessage = message as FunctionStartedMessage;

            if (functionStartedMessage != null)
            {
                Process(functionStartedMessage);
                return;
            }

            FunctionCompletedMessage functionCompletedMessage = message as FunctionCompletedMessage;

            if (functionCompletedMessage != null)
            {
                Process(functionCompletedMessage);
                return;
            }

            string errorMessage =
                String.Format(CultureInfo.InvariantCulture, "Unknown message type '{0}'.", message.Type);
            throw new InvalidOperationException(errorMessage);
        }

        private void Process(HostStartupMessage message)
        {
            Guid hostInstanceId = message.HostInstanceId;

            IEnumerable<FunctionDefinition> newFunctions = message.Functions;

            Guid hostId = message.HostId;

            IEnumerable<FunctionDefinition> existingFunctions = _functionTable.ReadAll().Where(f => f.HostId == hostId);

            DateTime hostInstanceCreatedOn = message.EnqueuedOn;

            IEnumerable<FunctionDefinition> functionsToDelete = existingFunctions.Where(f => !f.HostVersion.HasValue || f.HostVersion.Value < hostInstanceCreatedOn);

            foreach (FunctionDefinition deleteFunction in functionsToDelete)
            {
                _functionTable.Delete(deleteFunction);
            }

            DateTime mostRecentHostInstanceDate = existingFunctions.Where(f => f.HostVersion.HasValue).Select(f => f.HostVersion.Value).OrderByDescending(d => d).FirstOrDefault();

            if (hostInstanceCreatedOn > mostRecentHostInstanceDate)
            {
                foreach (FunctionDefinition functionToAdd in newFunctions)
                {
                    functionToAdd.HostId = hostId;
                    functionToAdd.HostVersion = hostInstanceCreatedOn;
                    _functionTable.Add(functionToAdd);
                }
            }
        }

        private void Process(FunctionStartedMessage message)
        {
            _functionInstanceLogger.LogFunctionStarted(message.LogEntity);
        }

        private void Process(FunctionCompletedMessage message)
        {
            _functionInstanceLogger.LogFunctionCompleted(message.LogEntity);
        }
    }
}