using System;
using System.Globalization;
using Dashboard.Data;
using Microsoft.Azure.Jobs;
using Microsoft.Azure.Jobs.Protocols;

namespace Dashboard.Indexers
{
    internal class Indexer : IIndexer
    {
        private readonly IPersistentQueueReader<PersistentQueueMessage> _queueReader;
        private readonly IHostInstanceLogger _hostInstanceLogger;
        private readonly IFunctionInstanceLogger _functionInstanceLogger;
        private readonly IFunctionInstanceLookup _functionInstanceLookup;
        private readonly IFunctionStatisticsWriter _statisticsWriter;
        private readonly IRecentFunctionWriter _indexWriter;
        private readonly ICausalityLogger _causalityLogger;
        private readonly IFunctionsInJobIndexer _functionsInJobIndexer;
        private readonly IExecutionStatsAggregator _executionStatsAggregator;

        public Indexer(IPersistentQueueReader<PersistentQueueMessage> queueReader,
            IHostInstanceLogger hostInstanceLogger, IFunctionInstanceLogger functionInstanceLogger,
            IFunctionInstanceLookup functionInstanceLookup, IFunctionStatisticsWriter statisticsWriter,
            IRecentFunctionWriter indexWriter, ICausalityLogger causalityLogger,
            IFunctionsInJobIndexer functionsInJobIndexer, IExecutionStatsAggregator executionStatsAggregator)
        {
            _queueReader = queueReader;
            _hostInstanceLogger = hostInstanceLogger;
            _functionInstanceLogger = functionInstanceLogger;
            _functionInstanceLookup = functionInstanceLookup;
            _statisticsWriter = statisticsWriter;
            _indexWriter = indexWriter;
            _causalityLogger = causalityLogger;
            _functionsInJobIndexer = functionsInJobIndexer;
            _executionStatsAggregator = executionStatsAggregator;
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
                Process(hostStartedMessage);
                return;
            }

            FunctionCompletedMessage functionCompletedMessage = message as FunctionCompletedMessage;

            if (functionCompletedMessage != null)
            {
                Process(functionCompletedMessage);
                return;
            }

            FunctionStartedMessage functionStartedMessage = message as FunctionStartedMessage;

            if (functionStartedMessage != null)
            {
                Process(functionStartedMessage);
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

            Guid functionInstanceId = message.FunctionInstanceId;

            if (!HasLoggedFunctionCompleted(functionInstanceId))
            {
                _indexWriter.CreateOrUpdate(message.StartTime, functionInstanceId);
            }

            if (message.WebJobRunIdentifier != null)
            {
                _functionsInJobIndexer.RecordFunctionInvocationForJobRun(functionInstanceId,
                    message.StartTime.UtcDateTime, message.WebJobRunIdentifier);
            }

            _executionStatsAggregator.LogFunctionStarted(message);
        }

        private bool HasLoggedFunctionCompleted(Guid functionInstanceId)
        {
            FunctionInstanceSnapshot primaryLog = _functionInstanceLookup.Lookup(functionInstanceId);

            return primaryLog != null && primaryLog.EndTime.HasValue;
        }

        internal static TriggerReason CreateTriggerReason(FunctionStartedMessage message)
        {
            return new TriggerReason
            {
                ChildGuid = message.FunctionInstanceId,
                ParentGuid = message.ParentId.GetValueOrDefault(),
                Message = message.FormatReason()
            };
        }

        private void Process(FunctionCompletedMessage message)
        {
            _functionInstanceLogger.LogFunctionCompleted(message);

            if (message.StartTime.Ticks != message.EndTime.Ticks)
            {
                _indexWriter.DeleteIfExists(message.StartTime, message.FunctionInstanceId);
            }

            _indexWriter.CreateOrUpdate(message.EndTime, message.FunctionInstanceId);

            _executionStatsAggregator.LogFunctionCompleted(message);

            string functionId = new FunctionIdentifier(message.SharedQueueName, message.Function.Id).ToString();

            // Increment is non-idempotent. If the process dies before deleting the message that triggered it, it can
            // occur multiple times.
            if (message.Succeeded)
            {
                _statisticsWriter.IncrementSuccess(functionId);
            }
            else
            {
                _statisticsWriter.IncrementFailure(functionId);
            }
        }
    }
}
