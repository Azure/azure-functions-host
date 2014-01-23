using System;

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

            var worker = new Worker(functionTable, executor);
            return worker;
        }
    }
}
