using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class FunctionStatisticsWriter :  IFunctionStatisticsWriter
    {
        private readonly IVersionedDocumentStore<FunctionStatistics> _store;

        [CLSCompliant(false)]
        public FunctionStatisticsWriter(CloudBlobClient client)
            : this(VersionedDocumentStore.CreateJsonBlobStore<FunctionStatistics>(
                client, DashboardContainerNames.Dashboard, DashboardDirectoryNames.FunctionStatistics))
        {
        }

        private FunctionStatisticsWriter(IVersionedDocumentStore<FunctionStatistics> store)
        {
            _store = store;
        }

        public void IncrementSuccess(string functionId)
        {
            UpdateEntity(functionId, (e) => e.SucceededCount++);
        }

        public void IncrementFailure(string functionId)
        {
            UpdateEntity(functionId, (e) => e.FailedCount++);
        }

        private void UpdateEntity(string functionId, Action<FunctionStatistics> modifier)
        {
            // Keep racing to update the entity until it succeeds.
            while (!TryUpdateEntity(functionId, modifier));
        }

        private bool TryUpdateEntity(string functionId, Action<FunctionStatistics> modifier)
        {
            VersionedDocument<FunctionStatistics> result = _store.Read(functionId);

            if (result == null || result.Document == null)
            {
                FunctionStatistics statistics = new FunctionStatistics();
                modifier.Invoke(statistics);
                return _store.TryCreate(functionId, statistics);
            }
            else
            {
                FunctionStatistics statistics = result.Document;
                modifier.Invoke(statistics);
                return _store.TryUpdate(functionId, statistics, result.ETag);
            }
        }
    }
}
