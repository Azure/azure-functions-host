using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class RecentFunctionWriter : IRecentFunctionWriter
    {
        private readonly IVersionedTextStore _store;

        [CLSCompliant(false)]
        public RecentFunctionWriter(CloudBlobClient client)
            : this(VersionedTextStore.CreateBlobStore(client, DashboardContainerNames.RecentFunctionsContainerName))
        {
        }

        private RecentFunctionWriter(IVersionedTextStore store)
        {
            _store = store;
        }

        public void CreateOrUpdate(DateTimeOffset timestamp, Guid id)
        {
            string innerId = CreateInnerId(timestamp, id);
            _store.CreateOrUpdate(innerId, String.Empty);
        }

        public void DeleteIfExists(DateTimeOffset timestamp, Guid id)
        {
            string innerId = CreateInnerId(timestamp, id);
            _store.DeleteIfExists(innerId);
        }

        private static string CreateInnerId(DateTimeOffset timestamp, Guid id)
        {
            return RecentFunctionInstance.Format(timestamp, id);
        }
    }
}
