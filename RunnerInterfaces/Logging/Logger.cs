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
    // Logs 
    // !!! Rename to be consistent
    public class FunctionInvokeLogger : IFunctionUpdatedLogger
    {
        private readonly IAzureTable<ExecutionInstanceLogEntity> _table;

        public FunctionInvokeLogger(IAzureTable<ExecutionInstanceLogEntity> table)
        {
            if (table == null)
            {
                throw new ArgumentNullException("table");
            }
            _table = table;
        }
                       
        // This may be called multiple times as a function execution is processed (queued, exectuing, completed, etc)
        public void Log(ExecutionInstanceLogEntity log)
        {
            string partKey = "1";
            string rowKey = log.GetKey();
            var l2 = _table.Lookup(partKey, rowKey);            
            if (l2 == null)
            {
                l2 = log;
            }
            else
            {
                // Merge
                Merge(l2, log);
            }

            _table.Write(partKey, rowKey, l2);
            _table.Flush();
        }

        // $$$ Should be a merge. Move this merge operation in IAzureTable?
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