using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs;
using Microsoft.Azure.Jobs.Protocols;

namespace Dashboard.Data
{
    // Includes both reading and writing the secondary indices together. 
    internal class ExecutionStatsAggregator : IFunctionInstanceLogger
    {
        private const string PartitionKey = "1";

        private readonly IFunctionStatisticsWriter _statisticsWriter;

        // 2nd index for most-recently used functions.
        // These are all sorted by time stamp.
        private readonly IAzureTable<FunctionInstanceGuid> _tableMRU;
        private readonly IAzureTable<FunctionInstanceGuid> _tableMRUByFunction;

        // Lookup in primary index
        private readonly IFunctionInstanceLookup _functionInstanceLookup;

        // Pass in table names for the various indices.
        public ExecutionStatsAggregator(
            IFunctionInstanceLookup functionInstanceLookup,
            IFunctionStatisticsWriter statisticsWriter,
            IAzureTable<FunctionInstanceGuid> tableMru,
            IAzureTable<FunctionInstanceGuid> tableMruByFunction)
        {
            NotNull(functionInstanceLookup, "functionInstanceLookup");
            NotNull(statisticsWriter, "statisticsWriter");
            NotNull(tableMru, "tableMru");
            NotNull(tableMruByFunction, "tableMruByFunction");

            _functionInstanceLookup = functionInstanceLookup;
            _statisticsWriter = statisticsWriter;
            _tableMRU = tableMru;
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
            _tableMRU.Flush();
            _tableMRUByFunction.Flush();
        }

        private void LogMru(FunctionStartedMessage message)
        {
            Guid instance = message.FunctionInstanceId;

            DateTime rowKeyTimestamp = message.StartTime.UtcDateTime;

            // Use function's actual end time (so we can reindex)
            // and append with the function instance ID just in case there are ties. 
            string rowKey = TableClient.GetTickRowKey(rowKeyTimestamp, instance);

            var ptr = new FunctionInstanceGuid(instance);
            _tableMRU.Write(PartitionKey, rowKey, ptr);

            string funcId = new FunctionIdentifier(message.SharedQueueName, message.Function.Id).ToString(); // valid row key
            _tableMRUByFunction.Write(funcId, rowKey, ptr);
        }

        private void LogMru(FunctionCompletedMessage message)
        {
            Guid instance = message.FunctionInstanceId;

            DateTime rowKeyTimestamp = message.EndTime.UtcDateTime;

            // Use function's actual end time (so we can reindex)
            // and append with the function instance ID just in case there are ties. 
            string rowKey = TableClient.GetTickRowKey(rowKeyTimestamp, instance);

            var ptr = new FunctionInstanceGuid(instance);
            _tableMRU.Write(PartitionKey, rowKey, ptr);

            string funcId = new FunctionIdentifier(message.SharedQueueName, message.Function.Id).ToString(); // valid row key
            _tableMRUByFunction.Write(funcId, rowKey, ptr);
        }

        private void DeleteIndex(string rowKey, string functionId)
        {
            _tableMRU.Delete(PartitionKey, rowKey);
            _tableMRUByFunction.Delete(TableClient.GetAsTableKey(functionId), rowKey);
        }

        public void LogFunctionStarted(FunctionStartedMessage mesage)
        {
            // This method may be called concurrently with LogFunctionCompleted.
            // Don't log a function running after it has been logged as completed.
            if (HasLoggedFunctionCompleted(mesage.FunctionInstanceId))
            {
                return;
            }

            LogMru(mesage);
            Flush();
        }

        private bool HasLoggedFunctionCompleted(Guid functionInstanceId)
        {
            FunctionInstanceSnapshot primaryLog = _functionInstanceLookup.Lookup(functionInstanceId);

            DateTimeOffset? completedTime = primaryLog != null ? primaryLog.EndTime : null;

            if (!completedTime.HasValue)
            {
                return false;
            }

            string completedRowKey = TableClient.GetTickRowKey(completedTime.Value.UtcDateTime, functionInstanceId);
            FunctionInstanceGuid completedRow = _tableMRU.Lookup(PartitionKey, completedRowKey);

            return !object.ReferenceEquals(completedRow, null);
        }

        public void LogFunctionCompleted(FunctionCompletedMessage message)
        {
            LogMru(message);

            // This method may be called concurrently with LogFunctionStarted.
            // Remove the function running log after logging as completed.
            DeleteFunctionStartedIfExists(message);

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
