using System;
using System.Collections.Generic;
using System.IO;
using Executor;
using Microsoft.WindowsAzure.StorageClient;
using Orchestrator;
using RunnerInterfaces;
using System.Linq;
using Newtonsoft.Json;
using System.Threading;
using AzureTables;
using Microsoft.WindowsAzure;
using SimpleBatch;

namespace DaasEndpoints
{
    // Access to the function table.
    // This is what the Indexer populates when it scans functions.
    // Orchestrator must read this to scan for triggers
    // Web dashboard may need this to show availability. 
    // Executor/RunnerHost shouldn't need this. 
    internal class FunctionTable : IFunctionTable
    {
        private readonly IAzureTable<FunctionDefinition> _table;

        private const string PartionKey = "1";

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
            FunctionDefinition func = _table.Lookup(PartionKey, functionId);
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
                throw new ArgumentNullException("func ");
            }

            // $$$ Batch this (AzureTable would handle that)
            string rowKey = func.ToString();
            _table.Write(PartionKey, rowKey, func);
            _table.Flush();
        }

        public virtual void Delete(FunctionDefinition func)
        {
            if (func == null)
            {
                throw new ArgumentNullException("func ");
            }

            string rowKey = func.ToString();
            _table.Delete(PartionKey, rowKey);
        }
    }
}
