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
    // !!! Used for IIndexerSettings (read/write function table), IOrchestratorSettings (Read function table)  
    // Services related to logging
    public partial class Services
    {
        // Get list of all registered functions.     
        // $$$ Merge with CloudIndexerSettings
        public FunctionIndexEntity[] GetFunctions()
        {
            var funcs = Utility.ReadTable<FunctionIndexEntity>(_account, EndpointNames.FunctionIndexTableName);
            return funcs;
        }

        // !!! Static lookups
        public FunctionIndexEntity Lookup(FunctionLocation location)
        {
            string rowKey = FunctionIndexEntity.GetRowKey(location);
            return Lookup(rowKey);
        }

        public FunctionIndexEntity Lookup(string functionId)
        {
            FunctionIndexEntity func = Utility.Lookup<FunctionIndexEntity>(
                _account,
                EndpointNames.FunctionIndexTableName,
                FunctionIndexEntity.PartionKey,
                functionId);
            return func;
        }
    }
}
