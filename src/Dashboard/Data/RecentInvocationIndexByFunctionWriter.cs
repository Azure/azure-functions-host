using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class RecentInvocationIndexByFunctionWriter : IRecentInvocationIndexByFunctionWriter
    {
        private readonly IVersionedTextStore _store;

        [CLSCompliant(false)]
        public RecentInvocationIndexByFunctionWriter(CloudBlobClient client)
            : this(VersionedTextStore.CreateBlobStore(
                client, DashboardContainerNames.Dashboard, DashboardDirectoryNames.RecentFunctionsByFunction))
        {
        }

        private RecentInvocationIndexByFunctionWriter(IVersionedTextStore store)
        {
            _store = store;
        }

        public void CreateOrUpdate(string functionId, DateTimeOffset timestamp, Guid id)
        {
            string innerId = CreateInnerId(functionId, timestamp, id);
            _store.CreateOrUpdate(innerId, String.Empty);
        }

        public void DeleteIfExists(string functionId, DateTimeOffset timestamp, Guid id)
        {
            string innerId = CreateInnerId(functionId, timestamp, id);
            _store.DeleteIfExists(innerId);
        }

        private static string CreateInnerId(string functionId, DateTimeOffset timestamp, Guid id)
        {
            return DashboardBlobPrefixes.CreateByFunctionRelativePrefix(functionId) +
                RecentInvocationEntry.Format(timestamp, id);
        }
    }
}
