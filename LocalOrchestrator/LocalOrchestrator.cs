using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using AzureTables;
using Executor;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using RunnerHost;
using RunnerInterfaces;
using SimpleBatch;
using SimpleBatch.Client;

namespace Orchestrator
{
    // Create a local orchestrator that can poll. 
    public class LocalOrchestrator
    {
        // Build by indexing all methods in type
        public static Worker Build(CloudStorageAccount account, Type typeClass)
        {
            var acs = account.ToString(true);
            var lc = new LocalExecutionContext(acs, typeClass);
            
            LocalFunctionTable store = new LocalFunctionTable(account);
            Indexer i = new Indexer(store);

            i.IndexType(store.OnApplyLocationInfo, typeClass);
            IFunctionTable functionTable = store; // $$$ Merge with LC
            IQueueFunction executor = lc.QueueFunction;

            var worker = new Worker(functionTable, executor);
            return worker;        
        }      
    }
}
