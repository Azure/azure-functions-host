using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class FunctionStatisticsReader :  IFunctionStatisticsReader
    {
        private readonly IConcurrentDocumentStore<FunctionStatistics> _store;

        [CLSCompliant(false)]
        public FunctionStatisticsReader(CloudBlobClient client)
            : this(ConcurrentDocumentStore.CreateJsonBlobStore<FunctionStatistics>(
                client, DashboardContainerNames.Dashboard, DashboardDirectoryNames.FunctionStatistics))
        {
        }

        private FunctionStatisticsReader(IConcurrentDocumentStore<FunctionStatistics> store)
        {
            _store = store;
        }

        public FunctionStatistics Lookup(string functionId)
        {
            IConcurrentDocument<FunctionStatistics> result = _store.Read(functionId);

            if (result == null)
            {
                return null;
            }

            return result.Document;
        }
    }
}
