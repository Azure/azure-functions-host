using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class RecentInvocationIndexByParentWriter : IRecentInvocationIndexByParentWriter
    {
        private readonly IVersionedTextStore _store;

        [CLSCompliant(false)]
        public RecentInvocationIndexByParentWriter(CloudBlobClient client)
            : this(VersionedTextStore.CreateBlobStore(client, DashboardContainerNames.RecentFunctionsContainerName))
        {
        }

        private RecentInvocationIndexByParentWriter(IVersionedTextStore store)
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
            return DashboardBlobPrefixes.CreateByParentPrefix(parentId) + RecentInvocationEntry.Format(timestamp, id);
        }
    }
}
