using System;
using System.Collections.Generic;
using System.Linq;
using AzureTables;

namespace Microsoft.WindowsAzure.Jobs
{
    // Includes both reading and writing the secondary indices together. 
    internal class ExecutionStatsAggregator : IFunctionInstanceLogger, IFunctionInstanceQuery
    {
        // in-memory cache of function entries. 
        // This corresponds to the azure table. 
        private Dictionary<FunctionLocation, FunctionStatsEntity> _funcs = new Dictionary<FunctionLocation, FunctionStatsEntity>();

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

        // Creates an instance that justs supports IFunctionInstanceLookup.
        public ExecutionStatsAggregator(
            IAzureTableReader<ExecutionInstanceLogEntity> tableLookup)
        {
            NotNull(tableLookup, "tableLookup");
            _tableLookup = tableLookup;
        }

        // Pass in table names for the various indices. 
        public ExecutionStatsAggregator(
            IAzureTableReader<ExecutionInstanceLogEntity> tableLookup,
            AzureTable<FunctionLocation, FunctionStatsEntity> tableStatsSummary,
            IAzureTable<FunctionInstanceGuid> tableMru,
            IAzureTable<FunctionInstanceGuid> tableMruByFunction,
            IAzureTable<FunctionInstanceGuid> tableMruByFunctionSucceeded,
            IAzureTable<FunctionInstanceGuid> tableMruFunctionFailed)
            : this(tableLookup)
        {
            NotNull(tableStatsSummary, "tableStatsSummary");
            NotNull(tableMru, "tableMru");
            NotNull(tableMruByFunction, "tableMruByFunction");
            NotNull(tableMruByFunctionSucceeded, "tableMruByFunctionSucceeded");
            NotNull(tableMruFunctionFailed, "tableMruFunctionFailed");

            _table = tableStatsSummary;
            _tableMRU = tableMru;
            _tableMRUByFunction = tableMruByFunction;
            _tableMRUByFunctionSucceed = tableMruByFunctionSucceeded;
            _tableMRUByFunctionFailed = tableMruFunctionFailed;
        }

        static void NotNull(object o, string paramName)
        {
            if (o == null)
            {
                throw new ArgumentNullException(paramName);
            }
        }

        // Lookup in the primary table.
        ExecutionInstanceLogEntity IFunctionInstanceLookup.Lookup(Guid rowKey)
        {
            return FunctionUpdatedLogger.RawLookup(_tableLookup, rowKey.ToString());
        }

        // This is the inverse operation of LogMru.
        // Lookup using secondary indices.
        IEnumerable<ExecutionInstanceLogEntity> IFunctionInstanceQuery.GetRecent(int N, FunctionInstanceQueryFilter filter)
        {
            if (_tableMRU == null)
            {
                // Instantiated this object with the wrong ctor.
                throw new InvalidOperationException("Secondary indices not set");
            }

            // FunctionLocation 
            // Success?

            IEnumerable<FunctionInstanceGuid> ptrs;
            if (filter.Location != null)
            {
                // Filter on a specific type of function 
                IAzureTableReader<FunctionInstanceGuid> table;

                if (filter.Succeeded.HasValue)
                {
                    if (filter.Succeeded.Value)
                    {
                        table = _tableMRUByFunctionSucceed;
                    }
                    else
                    {
                        table = _tableMRUByFunctionFailed;
                    }
                }
                else
                {
                    table = _tableMRUByFunction;
                }

                string funcId = filter.Location.ToString(); // valid row key

                ptrs = table.Enumerate(funcId);
            }
            else
            {
                IAzureTableReader<FunctionInstanceGuid> table = _tableMRU;
                // Take all functions, without filter. 
                ptrs = table.Enumerate();
            }

            ptrs = ptrs.Take(N);

            IFunctionInstanceLookup lookup = this;
            return from ptr in ptrs
                   let val = lookup.Lookup(ptr)
                   where val != null
                   select val;
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

        private string LogMru(ExecutionInstanceLogEntity log)
        {
            Guid instance = log.FunctionInstance.Id;

            Dictionary<string, string> d = new Dictionary<string, string>();

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
                rowKeyTimestamp = DateTime.UtcNow;
            }

            // Use function's actual end time (so we can reindex)
            // and append with Now just in case there are ties. 
            string rowKey = TableClient.GetTickRowKey(rowKeyTimestamp);

            var ptr = new FunctionInstanceGuid(log);
            _tableMRU.Write("1", rowKey, ptr);

            string funcId = log.FunctionInstance.Location.ToString(); // valid row key
            _tableMRUByFunction.Write(funcId, rowKey, ptr);

            switch (log.GetStatus())
            {
                case FunctionInstanceStatus.CompletedSuccess:
                    _tableMRUByFunctionSucceed.Write(funcId, rowKey, ptr);
                    break;
                case FunctionInstanceStatus.CompletedFailed:
                    _tableMRUByFunctionFailed.Write(funcId, rowKey, ptr);
                    break;
            }

            return rowKey;
        }

        private void DeleteIndex(string rowKey, ExecutionInstanceLogEntity log)
        {
            _tableMRU.Delete("1", rowKey);
            _tableMRUByFunction.Delete(log.FunctionInstance.Location.ToString(), rowKey);
        }

        string IndexRunningFunction(ExecutionInstanceLogEntity log)
        {
            return LogMru(log);
        }

        void IndexCompletedFunction(ExecutionInstanceLogEntity log)
        {
            LogMru(log);

            if (!log.IsCompleted())
            {
                return;
            }

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

            switch (log.GetStatus())
            {
                case FunctionInstanceStatus.CompletedSuccess:
                    stats.CountCompleted++;
                    if (log.GetDuration().HasValue)
                    {
                        stats.Runtime += log.GetDuration().Value;
                    }
                    stats.LastWriteTime = DateTime.UtcNow;
                    break;
                case FunctionInstanceStatus.CompletedFailed:
                    stats.CountErrors++;
                    break;
            }
        }

        IFunctionInstanceLoggerContext IFunctionInstanceLogger.CreateContext(ExecutionInstanceLogEntity func)
        {
            return new InstanceContext(this, func);
        }

        // Tracks the function instance as it is logged.
        // For example, logging a completed function deletes old log items from the running state.
        private class InstanceContext : IFunctionInstanceLoggerContext
        {
            private readonly ExecutionStatsAggregator _parent;
            private readonly ExecutionInstanceLogEntity _logItem;

            private string rowKey;

            public InstanceContext(ExecutionStatsAggregator parent, ExecutionInstanceLogEntity logItem)
            {
                if (parent == null)
                {
                    throw new ArgumentNullException("parent");
                }

                if (logItem == null)
                {
                    throw new ArgumentNullException("logItem");
                }

                _parent = parent;
                _logItem = logItem;
            }

            public void IndexRunningFunction()
            {
                rowKey = _parent.IndexRunningFunction(_logItem);
            }

            public void IndexCompletedFunction()
            {
                if (rowKey != null)
                {
                    _parent.DeleteIndex(rowKey, _logItem);
                }

                _parent.IndexCompletedFunction(_logItem);
            }

            public void Flush()
            {
                _parent.Flush();
            }
        }
    }
}
