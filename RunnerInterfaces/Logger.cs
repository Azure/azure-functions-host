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
using SimpleBatch;

namespace Executor
{
    // ### This should just be an azure table (maybe with trivial wrapper for Row/Part key)
    public class FunctionInvokeLogger : IFunctionUpdatedLogger
    {
        public CloudStorageAccount _account;
        
        // Table that lists all functions. 
        public string _tableName;
                
        // This may be called multiple times as a function execution is processed (queued, exectuing, completed, etc)
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
    }    
}