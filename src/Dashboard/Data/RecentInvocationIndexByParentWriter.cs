using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class RecentInvocationIndexByParentWriter : IRecentInvocationIndexByParentWriter
    {
        private readonly IConcurrentTextStore _store;

        [CLSCompliant(false)]
        public RecentInvocationIndexByParentWriter(CloudBlobClient client)
            : this(ConcurrentTextStore.CreateBlobStore(
                client, DashboardContainerNames.Dashboard, DashboardDirectoryNames.RecentFunctionsByParent))
        {
        }

        private RecentInvocationIndexByParentWriter(IConcurrentTextStore store)
        {
            _store = store;
        }

        public void CreateOrUpdate(Guid parentId, DateTimeOffset timestamp, Guid id)
        {
            string innerId = CreateInnerId(parentId, timestamp, id);
            _store.CreateOrUpdate(innerId, String.Empty);
        }

        public void DeleteIfExists(Guid parentId, DateTimeOffset timestamp, Guid id)
        {
            string innerId = CreateInnerId(parentId, timestamp, id);
            _store.DeleteIfExists(innerId);
        }

        private static string CreateInnerId(Guid parentId, DateTimeOffset timestamp, Guid id)
        {
            return DashboardBlobPrefixes.CreateByParentRelativePrefix(parentId) +
                RecentInvocationEntry.Format(timestamp, id);
        }
    }
}
