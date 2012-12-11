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
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure;

namespace DaasEndpoints
{
    // Access to the function table.
    // This is what the Indexer populates when it scans functions.
    // Orchestrator must read this to scan for triggers
    // Web dashboard may need this to show availability. 
    // Executor/RunnerHost shouldn't need this. 
    public class FunctionTable : IFunctionTable
    {
        private readonly CloudStorageAccount _account;
        private string _tableName;

        public FunctionTable(CloudStorageAccount account, string tableName)
        {
            _account = account;
            _tableName = tableName;
        }

        public virtual FunctionIndexEntity Lookup(string functionId)
        {
            FunctionIndexEntity func = Utility.Lookup<FunctionIndexEntity>(
              _account,
              _tableName,
              FunctionIndexEntity.PartionKey,
              functionId);
            return func;
        }

        public virtual FunctionIndexEntity[] ReadAll()
        {
            var funcs = Utility.ReadTable<FunctionIndexEntity>(_account, EndpointNames.FunctionIndexTableName);
            return funcs;
        }

        public virtual void Add(FunctionIndexEntity func)
        {
            // $$$ Batch this (AzureTable would handle that)
            Utility.AddTableRow(_account, _tableName, func);
        }

        public virtual void Delete(FunctionIndexEntity func)
        {
            Utility.DeleteTableRow(_account, _tableName, func);
        }
    }
}
