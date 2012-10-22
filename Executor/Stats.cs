using System;
using System.Collections.Generic;
using System.Data.Services.Common;
using System.Linq;
using System.Reflection;
using System.Text;
using AzureTables;
using Executor;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;

namespace Executor
{
    // ### 2 tables? Alltime, Last 24 hours (or since some reset point)
    public class ExecutionStatsAggregator
    {
        // in-memory cache of function entries. 
        // This corresponds to the azure table. 
        private Dictionary<FunctionLocation, FunctionStatsEntity> _funcs = new Dictionary<FunctionLocation,FunctionStatsEntity>();

        private readonly FunctionInvokeLogger _logger;

        private readonly AzureTable<FunctionLocation, FunctionStatsEntity> _table;

        // 2nd index for most-recently used functions
        private readonly AzureTable _tableMRU;

        public ExecutionStatsAggregator(AzureTable table, FunctionInvokeLogger invokeLogger, AzureTable tableMRU)
        {
            _tableMRU = tableMRU;
            _logger = invokeLogger;
            _table = table.GetTypeSafeWrapper<FunctionLocation, FunctionStatsEntity>( 
                row => Tuple.Create("1", row.ToString()));
        }

        public void Flush()
        {
            _tableMRU.Flush();

            foreach (var kv in _funcs)
            {
                _table.Add(kv.Key, kv.Value);
            }
            _table.Flush();

            _funcs.Clear(); // cause it to be reloaded 
        }

        private void LogMru(Guid instance)
        {            
            Dictionary<string, string> d = new Dictionary<string,string>();
            string rowKey = string.Format("{0:D19}", DateTime.MaxValue.Ticks - DateTime.UtcNow.Ticks);

            d["instance"] = instance.ToString();
            _tableMRU.Write("1", rowKey, d);
        }

        public void OnFunctionComplete(Guid instance)
        {
            var log = _logger.Get(instance);

            LogMru(instance);

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
