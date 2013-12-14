using System;
using Microsoft.WindowsAzure;


namespace Microsoft.WindowsAzure.Jobs
{
    // Create a local orchestrator that can poll. 
    internal class LocalOrchestrator
    {
        // Build by indexing all methods in type
        internal static Worker Build(CloudStorageAccount account, Type typeClass)
        {
            var acs = account.ToString(true);
            var lc = new LocalExecutionContext(acs, typeClass);
            
            LocalFunctionTable store = new LocalFunctionTable(account);
            Indexer i = new Indexer(store);

            i.IndexType(store.OnApplyLocationInfo, typeClass);
            IFunctionTable functionTable = store; // $$$ Merge with LC
            IQueueFunction executor = lc.QueueFunction;

            var worker = new Worker(typeClass.Assembly.FullName, functionTable, new NullRunningHostTableWriter(), executor);
            return worker;        
        }

        private class NullRunningHostTableWriter : IRunningHostTableWriter
        {
            public void SignalHeartbeat(string hostName)
            {
            }
        }
    }
}
