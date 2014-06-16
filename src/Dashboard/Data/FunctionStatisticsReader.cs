using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class FunctionStatisticsReader :  IFunctionStatisticsReader
    {
        private readonly IVersionedDocumentStore<FunctionStatistics> _store;

        [CLSCompliant(false)]
        public FunctionStatisticsReader(CloudBlobClient client)
            : this(client.GetContainerReference(DashboardContainerNames.FunctionStatisticsContainer))
        {
        }

        private FunctionStatisticsReader(CloudBlobContainer container)
        {
            _store = new VersionedDocumentStore<FunctionStatistics>(container);
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
