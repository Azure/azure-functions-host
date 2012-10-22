using System;
using System.Collections.Generic;
using System.Data.Services.Common;
using System.Linq;
using System.Text;
using AzureTables;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using RunnerInterfaces;

namespace Executor
{
    // ### This should just be an azure table (maybe with trivial wrapper for Row/Part key)
    public class FunctionInvokeLogger
    {
        public CloudStorageAccount _account;
        public string _tableName;

        public AzureTable _tableMRU;

        public void Log(ExecutionInstanceLogEntity log)
        {
            // $$$ Should be a merge. Is there a table operation for merge?

            var l2 = Utility.Lookup<ExecutionInstanceLogEntity>(_account, _tableName, log.PartitionKey, log.RowKey);
            if (l2 == null)
            {
                l2 = log;
            }
            else
            {
                // Merge
                Merge(l2, log);
            }

            Utility.AddTableRow(_account, _tableName, l2);
        }

        static void Merge<T>(T mutate, T delta)
        {
            foreach (var property in typeof(T).GetProperties())
            {
                var deltaVal = property.GetValue(delta, null);
                if (deltaVal != null)
                {
                    property.SetValue(mutate, deltaVal, null);
                }
            }
        }

        // Lookup a given execution instance. 
        public ExecutionInstanceLogEntity Get(Guid rowKey)
        {
            return Utility.Lookup<ExecutionInstanceLogEntity>(_account, _tableName, "1", rowKey.ToString());
        }

        // Sorted from newest to oldest
        public IEnumerable<ExecutionInstanceLogEntity> GetAll()
        {
            return GetRecent(int.MaxValue);
        }

        public ExecutionInstanceLogEntity[] GetRecent(int n)
        {
            var entries = _tableMRU.Enumerate().Take(n);

            List<ExecutionInstanceLogEntity> list = new List<ExecutionInstanceLogEntity>();
            foreach (var entry in entries)
            {
                Guid g = Guid.Parse(entry["instance"]);

                list.Add(Get(g));
            }
            return list.ToArray();

#if false
            var logs = Utility.ReadTable<ExecutionInstanceLogEntity>(_account, _tableName);

            DateTime[] time = Array.ConvertAll(logs, log => log.Timestamp);
            Array.Sort(time, logs); // sorts oldest to newest
            Array.Reverse(logs);

            return logs;
#endif
        }
    }
}