using System;
using Microsoft.Azure.Jobs;
using Microsoft.Azure.Jobs.Protocols;

namespace Dashboard.Data
{
    internal class ExecutionStatsAggregator : IExecutionStatsAggregator
    {
        // 2nd index for most-recently used functions.
        // These are all sorted by time stamp.
        private readonly IAzureTable<FunctionInstanceGuid> _tableMRUByFunction;

        // Lookup in primary index
        private readonly IFunctionInstanceLookup _functionInstanceLookup;

        // Pass in table names for the various indices.
        public ExecutionStatsAggregator(
            IFunctionInstanceLookup functionInstanceLookup,
            IAzureTable<FunctionInstanceGuid> tableMruByFunction)
        {
            NotNull(functionInstanceLookup, "functionInstanceLookup");
            NotNull(tableMruByFunction, "tableMruByFunction");

            _functionInstanceLookup = functionInstanceLookup;
            _tableMRUByFunction = tableMruByFunction;
        }

        private static void NotNull(object o, string paramName)
        {
            if (o == null)
            {
                throw new ArgumentNullException(paramName);
            }
        }

        public void Flush()
        {
            _tableMRUByFunction.Flush();
        }

        private void LogMru(FunctionStartedMessage message, DateTimeOffset timestamp)
        {
            Guid instanceId = message.FunctionInstanceId;

            DateTime rowKeyTimestamp = timestamp.UtcDateTime;

            // Use function's actual start,end time (so we can reindex)
            // and append with the function instance ID just in case there are ties. 
            string rowKey = TableClient.GetTickRowKey(rowKeyTimestamp, instanceId);

            var ptr = new FunctionInstanceGuid(instanceId);

            string funcId = new FunctionIdentifier(message.SharedQueueName, message.Function.Id).ToString(); // valid row key
            _tableMRUByFunction.Write(funcId, rowKey, ptr);
        }

        private void DeleteIndex(string rowKey, string functionId)
        {
            _tableMRUByFunction.Delete(TableClient.GetAsTableKey(functionId), rowKey);
        }

        public void LogFunctionStarted(FunctionStartedMessage message)
        {
            // This method may be called concurrently with LogFunctionCompleted.
            // Don't log a function running after it has been logged as completed.
            if (HasLoggedFunctionCompleted(message.FunctionInstanceId))
            {
                return;
            }

            LogMru(message, message.StartTime);
            Flush();
        }

        private bool HasLoggedFunctionCompleted(Guid functionInstanceId)
        {
            FunctionInstanceSnapshot primaryLog = _functionInstanceLookup.Lookup(functionInstanceId);

            DateTimeOffset? completedTime = primaryLog != null ? primaryLog.EndTime : null;

            return completedTime.HasValue;
        }

        public void LogFunctionCompleted(FunctionCompletedMessage message)
        {
            LogMru(message, message.EndTime);

            // This method may be called concurrently with LogFunctionStarted.
            // Remove the function running log after logging as completed.
            DeleteFunctionStartedIfExists(message);

            Flush();
        }

        private void DeleteFunctionStartedIfExists(FunctionCompletedMessage message)
        {
            if (message.StartTime.Ticks == message.EndTime.Ticks)
            {
                return;
            }

            DeleteIndex(TableClient.GetTickRowKey(message.StartTime.UtcDateTime, message.FunctionInstanceId), new FunctionIdentifier(message.SharedQueueName, message.Function.Id).ToString());
        }
    }
}
