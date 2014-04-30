using System;
using System.Linq;
using Microsoft.Azure.Jobs;

namespace Dashboard.Data
{
    // Access to the function table.
    // This is what the Indexer populates when it scans functions.
    // Orchestrator must read this to scan for triggers
    // Web dashboard may need this to show availability. 
    // Executor/RunnerHost shouldn't need this. 
    internal class FunctionTable : IFunctionTable
    {
        private readonly IAzureTable<FunctionDefinition> _table;

        private const string PartitionKey = "1";

        public FunctionTable(IAzureTable<FunctionDefinition> table)
        {
            _table = table;
        }

        public virtual FunctionDefinition Lookup(string functionId)
        {
            if (functionId == null)
            {
                throw new ArgumentNullException("functionId");
            }
            FunctionDefinition func = _table.Lookup(PartitionKey, functionId);
            return func;
        }

        public virtual FunctionDefinition[] ReadAll()
        {
            var all = _table.Enumerate().ToArray();
            return all;
        }

        public virtual void Add(FunctionDefinition func)
        {
            if (func == null)
            {
                throw new ArgumentNullException("func");
            }

            // $$$ Batch this (AzureTable would handle that)
            string rowKey = func.ToString();
            _table.Write(PartitionKey, rowKey, func);
            _table.Flush();
        }

        public virtual void Delete(FunctionDefinition func)
        {
            if (func == null)
            {
                throw new ArgumentNullException("func");
            }

            string rowKey = func.ToString();
            _table.Delete(PartitionKey, rowKey);
        }
    }
}
