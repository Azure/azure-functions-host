using System;
using System.Collections.Generic;
using System.Data.Services.Common;
using System.Linq;
using System.Reflection;
using System.Text;
using AzureTables;
using Executor;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;
using SimpleBatch;

namespace Executor
{
    // Includes both reading and writing the secondary indices together. 
    public class ExecutionStatsAggregator : IFunctionCompleteLogger, IFunctionInstanceQuery
    {
        // in-memory cache of function entries. 
        // This corresponds to the azure table. 
        private Dictionary<FunctionLocation, FunctionStatsEntity> _funcs = new Dictionary<FunctionLocation,FunctionStatsEntity>();

        private readonly AzureTable<FunctionLocation, FunctionStatsEntity> _table;

        // 2nd index for most-recently used functions.
        // These are all sorted by time stamp. 
        private readonly IAzureTable<FunctionInstanceGuid> _tableMRU;
        private readonly IAzureTable<FunctionInstanceGuid> _tableMRUByFunction;
        private readonly IAzureTable<FunctionInstanceGuid> _tableMRUByFunctionSucceed;
        private readonly IAzureTable<FunctionInstanceGuid> _tableMRUByFunctionFailed;

        // Lookup in primary index
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

        void IFunctionCompleteLogger.Flush()
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

        private void LogMru(ExecutionInstanceLogEntity log)
        {
            if (!log.IsCompleted())
            {
                return;
            }
            Guid instance = log.FunctionInstance.Id;

            Dictionary<string, string> d = new Dictionary<string,string>();
                        
            // Use function's actual end time (so we can reindex)
            // and append with Now just in case there are ties. 
            string rowKey = Utility.GetTickRowKey(log.EndTime.Value);

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

        }

        // Called by the orchestrator (which gaurantees single-threaded access) sometime shortly after a 
        // function finishes executing.
        void IFunctionCompleteLogger.IndexCompletedFunction(ExecutionInstanceLogEntity log)
        {   
            LogMru(log);

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
    }   

    // Statistics per function type, aggregated across all instances.
    // These must all be monotonically increasing numbers, since they're continually aggregated.
    // Eg, we can't do queud. 
    public class FunctionStatsEntity
    {
        public DateTime LastWriteTime { get; set; } // last time function was executed and succeeded

        public int CountCompleted { get; set; } // Total run            
        public int CountErrors { get; set; } // number of runs with failure status
        public TimeSpan Runtime { get; set; } // total time spent running.         
    }   
}
