using System;
using System.Globalization;
using Dashboard.Data;
using Microsoft.Azure.Jobs;
using Microsoft.Azure.Jobs.Protocols;

namespace Dashboard.Indexers
{
    internal class Indexer : IIndexer
    {
        private readonly IPersistentQueue<PersistentQueueMessage> _queue;
        private readonly IHostInstanceLogger _hostInstanceLogger;
        private readonly IFunctionInstanceLogger _functionInstanceLogger;
        private readonly ICausalityLogger _causalityLogger;
        private readonly IFunctionsInJobIndexer _functionsInJobIndexer;

        public Indexer(IPersistentQueue<PersistentQueueMessage> queue, IHostInstanceLogger hostInstanceLogger,
            IFunctionInstanceLogger functionInstanceLogger, ICausalityLogger causalityLogger,
            IFunctionsInJobIndexer functionsInJobIndexer)
        {
            _queue = queue;
            _hostInstanceLogger = hostInstanceLogger;
            _functionInstanceLogger = functionInstanceLogger;
            _causalityLogger = causalityLogger;
            _functionsInJobIndexer = functionsInJobIndexer;
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
            HostStartedMessage hostStartedMessage = message as HostStartedMessage;

            if (hostStartedMessage != null)
            {
                Process(hostStartedMessage);
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

        private void Process(HostStartedMessage message)
        {
            _hostInstanceLogger.LogHostStarted(message);
        }

        private void Process(FunctionStartedMessage message)
        {
            _functionInstanceLogger.LogFunctionStarted(message);
            _causalityLogger.LogTriggerReason(CreateTriggerReason(message));

            if (message.WebJobRunIdentifier != null)
            {
                _functionsInJobIndexer.RecordFunctionInvocationForJobRun(message.FunctionInstanceId,
                    message.StartTime.UtcDateTime, message.WebJobRunIdentifier);
            }
        }

        internal static TriggerReason CreateTriggerReason(FunctionStartedMessage message)
        {
            if (!message.ParentId.HasValue && String.IsNullOrEmpty(message.Reason))
            {
                return null;
            }

            return new InvokeTriggerReason
            {
                ChildGuid = message.FunctionInstanceId,
                ParentGuid = message.ParentId.GetValueOrDefault(),
                Message = message.Reason
            };
        }

        private void Process(FunctionCompletedMessage message)
        {
            _functionInstanceLogger.LogFunctionCompleted(message);
        }
    }
}