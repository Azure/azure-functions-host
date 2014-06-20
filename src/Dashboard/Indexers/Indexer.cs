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
        private readonly IRecentInvocationIndexWriter _recentInvocationsWriter;
        private readonly IRecentInvocationIndexByFunctionWriter _recentInvocationsByFunctionWriter;
        private readonly ICausalityLogger _causalityLogger;
        private readonly IFunctionsInJobIndexer _functionsInJobIndexer;

        public Indexer(IPersistentQueueReader<PersistentQueueMessage> queueReader,
            IHostInstanceLogger hostInstanceLogger, IFunctionInstanceLogger functionInstanceLogger,
            IFunctionInstanceLookup functionInstanceLookup, IFunctionStatisticsWriter statisticsWriter,
            IRecentInvocationIndexWriter recentInvocationsWriter,
            IRecentInvocationIndexByFunctionWriter recentInvocationsByFunctionWriter, ICausalityLogger causalityLogger,
            IFunctionsInJobIndexer functionsInJobIndexer)
        {
            _queueReader = queueReader;
            _hostInstanceLogger = hostInstanceLogger;
            _functionInstanceLogger = functionInstanceLogger;
            _functionInstanceLookup = functionInstanceLookup;
            _statisticsWriter = statisticsWriter;
            _recentInvocationsWriter = recentInvocationsWriter;
            _recentInvocationsByFunctionWriter = recentInvocationsByFunctionWriter;
            _causalityLogger = causalityLogger;
            _functionsInJobIndexer = functionsInJobIndexer;
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

            string functionId = new FunctionIdentifier(message.SharedQueueName, message.Function.Id).ToString();
            Guid functionInstanceId = message.FunctionInstanceId;
            DateTimeOffset startTime = message.StartTime;

            if (!HasLoggedFunctionCompleted(functionInstanceId))
            {
                _recentInvocationsWriter.CreateOrUpdate(startTime, functionInstanceId);
                _recentInvocationsByFunctionWriter.CreateOrUpdate(functionId, startTime, functionInstanceId);
            }

            if (message.WebJobRunIdentifier != null)
            {
                _functionsInJobIndexer.RecordFunctionInvocationForJobRun(functionInstanceId, startTime.UtcDateTime,
                    message.WebJobRunIdentifier);
            }
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

            string functionId = new FunctionIdentifier(message.SharedQueueName, message.Function.Id).ToString();
            Guid functionInstanceId = message.FunctionInstanceId;
            DateTimeOffset startTime = message.StartTime;
            DateTimeOffset endTime = message.EndTime;

            if (startTime.Ticks != endTime.Ticks)
            {
                _recentInvocationsWriter.DeleteIfExists(startTime, functionInstanceId);
                _recentInvocationsByFunctionWriter.DeleteIfExists(functionId, startTime, functionInstanceId);
            }

            _recentInvocationsWriter.CreateOrUpdate(endTime, functionInstanceId);
            _recentInvocationsByFunctionWriter.CreateOrUpdate(functionId, endTime, functionInstanceId);

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
