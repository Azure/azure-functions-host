using System;
using System.Collections.Generic;
using System.Linq;
using AzureTables;
using Microsoft.Azure.Jobs;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Dashboard.Data
{
    // Includes both reading and writing the secondary indices together. 
    internal class ExecutionStatsAggregator : IFunctionInstanceLogger
    {
        private const string PartitionKey = "1";

        // in-memory cache of function entries.
        // This corresponds to the azure table.
        private Dictionary<FunctionLocation, FunctionStatsEntity> _funcs =
            new Dictionary<FunctionLocation, FunctionStatsEntity>();

        private readonly AzureTable<FunctionLocation, FunctionStatsEntity> _table;

        // 2nd index for most-recently used functions.
        // These are all sorted by time stamp.
        private readonly IAzureTable<FunctionInstanceGuid> _tableMRU;
        private readonly IAzureTable<FunctionInstanceGuid> _tableMRUByFunction;
        private readonly IAzureTable<FunctionInstanceGuid> _tableMRUByFunctionSucceed;
        private readonly IAzureTable<FunctionInstanceGuid> _tableMRUByFunctionFailed;

        // Lookup in primary index
        // $$$ Should this be IFunctionInstanceLookup instead?
        private readonly IAzureTableReader<ExecutionInstanceLogEntity> _tableLookup;

        // Pass in table names for the various indices.
        public ExecutionStatsAggregator(
            IAzureTableReader<ExecutionInstanceLogEntity> tableLookup,
            AzureTable<FunctionLocation, FunctionStatsEntity> tableStatsSummary,
            IAzureTable<FunctionInstanceGuid> tableMru,
            IAzureTable<FunctionInstanceGuid> tableMruByFunction,
            IAzureTable<FunctionInstanceGuid> tableMruByFunctionSucceeded,
            IAzureTable<FunctionInstanceGuid> tableMruFunctionFailed)
        {
            NotNull(tableLookup, "tableLookup");
            NotNull(tableStatsSummary, "tableStatsSummary");
            NotNull(tableMru, "tableMru");
            NotNull(tableMruByFunction, "tableMruByFunction");
            NotNull(tableMruByFunctionSucceeded, "tableMruByFunctionSucceeded");
            NotNull(tableMruFunctionFailed, "tableMruFunctionFailed");

            _tableLookup = tableLookup;
            _table = tableStatsSummary;
            _tableMRU = tableMru;
            _tableMRUByFunction = tableMruByFunction;
            _tableMRUByFunctionSucceed = tableMruByFunctionSucceeded;
            _tableMRUByFunctionFailed = tableMruFunctionFailed;
        }

        private static void NotNull(object o, string paramName)
        {
            if (o == null)
            {
                throw new ArgumentNullException(paramName);
            }
        }

        private ExecutionInstanceLogEntity LookupInPrimaryTable(Guid functionInstanceId)
        {
            return FunctionInstanceLookup.RawLookup(_tableLookup, functionInstanceId.ToString());
        }

        public void Flush()
        {
            _tableMRU.Flush();
            _tableMRUByFunction.Flush();
            _tableMRUByFunctionSucceed.Flush();
            _tableMRUByFunctionFailed.Flush();

            foreach (var kv in _funcs)
            {
                _table.Add(kv.Key, kv.Value);
            }
            _table.Flush();

            _funcs.Clear(); // cause it to be reloaded
        }

        private void LogMru(FunctionStartedSnapshot snapshot)
        {
            Guid instance = snapshot.FunctionInstanceId;

            DateTime rowKeyTimestamp = snapshot.StartTime.UtcDateTime;

            // Use function's actual end time (so we can reindex)
            // and append with the function instance ID just in case there are ties. 
            string rowKey = TableClient.GetTickRowKey(rowKeyTimestamp, instance);

            var ptr = new FunctionInstanceGuid(instance);
            _tableMRU.Write(PartitionKey, rowKey, ptr);

            string funcId = snapshot.FunctionId; // valid row key
            _tableMRUByFunction.Write(funcId, rowKey, ptr);
        }

        private void LogMru(ExecutionInstanceLogEntity log)
        {
            Guid instance = log.FunctionInstance.Id;

            DateTime rowKeyTimestamp;

            if (log.EndTime.HasValue)
            {
                rowKeyTimestamp = log.EndTime.Value;
            }
            else if (log.StartTime.HasValue)
            {
                rowKeyTimestamp = log.StartTime.Value;
            }
            else
            {
                rowKeyTimestamp = log.QueueTime;
            }

            // Use function's actual end time (so we can reindex)
            // and append with the function instance ID just in case there are ties. 
            string rowKey = TableClient.GetTickRowKey(rowKeyTimestamp, log.FunctionInstance.Id);

            var ptr = new FunctionInstanceGuid(log);
            _tableMRU.Write(PartitionKey, rowKey, ptr);

            string funcId = log.FunctionInstance.Location.ToString(); // valid row key
            _tableMRUByFunction.Write(funcId, rowKey, ptr);

            switch (log.GetStatusWithoutHeartbeat())
            {
                case FunctionInstanceStatus.CompletedSuccess:
                    _tableMRUByFunctionSucceed.Write(funcId, rowKey, ptr);
                    break;
                case FunctionInstanceStatus.CompletedFailed:
                    _tableMRUByFunctionFailed.Write(funcId, rowKey, ptr);
                    break;
            }
        }

        private void DeleteIndex(string rowKey, ExecutionInstanceLogEntity log)
        {
            _tableMRU.Delete(PartitionKey, rowKey);
            _tableMRUByFunction.Delete(log.FunctionInstance.Location.ToString(), rowKey);
        }

        public void LogFunctionStarted(FunctionStartedSnapshot snapshot)
        {
            // This method may be called concurrently with LogFunctionCompleted.
            // Don't log a function running after it has been logged as completed.
            if (HasLoggedFunctionCompleted(snapshot.FunctionInstanceId))
            {
                return;
            }

            LogMru(snapshot);
            Flush();
        }

        private bool HasLoggedFunctionCompleted(Guid functionInstanceId)
        {
            ExecutionInstanceLogEntity primaryLog = LookupInPrimaryTable(functionInstanceId);

            DateTime? completedTime = primaryLog.EndTime;

            if (!completedTime.HasValue)
            {
                return false;
            }

            string completedRowKey = TableClient.GetTickRowKey(completedTime.Value, functionInstanceId);
            FunctionInstanceGuid completedRow = _tableMRU.Lookup(PartitionKey, completedRowKey);

            return !object.ReferenceEquals(completedRow, null);
        }

        public void LogFunctionCompleted(ExecutionInstanceLogEntity log)
        {
            LogMru(log);

            if (!log.IsCompleted())
            {
                Flush();
                return;
            }

            // This method may be called concurrently with LogFunctionStarted.
            // Remove the function running log after logging as completed.
            DeleteFunctionStartedIfExists(log);

            FunctionLocation kind = log.FunctionInstance.Location;
            FunctionStatsEntity stats;
            if (!_funcs.TryGetValue(kind, out stats))
            {
                stats = _table.Lookup(log.FunctionInstance.Location);
                if (stats == null)
                {
                    stats = new FunctionStatsEntity();
                }
                _funcs[kind] = stats;
            }

            switch (log.GetStatusWithoutHeartbeat())
            {
                case FunctionInstanceStatus.CompletedSuccess:
                    stats.CountCompleted++;
                    TimeSpan? duration = log.GetFinalDuration();
                    if (duration.HasValue)
                    {
                        stats.Runtime += duration.Value;
                    }
                    stats.LastWriteTime = DateTime.UtcNow;
                    break;
                case FunctionInstanceStatus.CompletedFailed:
                    stats.CountErrors++;
                    break;
            }

            Flush();
        }

        private void DeleteFunctionStartedIfExists(ExecutionInstanceLogEntity logItem)
        {
            if (!logItem.StartTime.HasValue ||
                (logItem.StartTime.HasValue &&
                logItem.EndTime.HasValue &&
                logItem.StartTime.Value.Ticks == logItem.EndTime.Value.Ticks))
            {
                return;
            }

            DeleteIndex(TableClient.GetTickRowKey(logItem.StartTime.Value, logItem.FunctionInstance.Id), logItem);
        }
    }
}
