using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class FunctionStatisticsReader :  IFunctionStatisticsReader
    {
        private readonly IVersionedDocumentStore<FunctionStatistics> _store;

        [CLSCompliant(false)]
        public FunctionStatisticsReader(CloudBlobClient client)
            : this(VersionedDocumentStore.CreateJsonBlobStore<FunctionStatistics>(
                client, DashboardContainerNames.Dashboard, DashboardDirectoryNames.FunctionStatistics))
        {
        }

        private FunctionStatisticsReader(IVersionedDocumentStore<FunctionStatistics> store)
        {
            _store = store;
        }

        public FunctionStatistics Lookup(string functionId)
        {
            VersionedDocument<FunctionStatistics> result = _store.Read(functionId);

            if (result == null)
            {
                return null;
            }

            return result.Document;
        }
    }
}
