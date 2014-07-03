using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    internal class HostIndexManager : IHostIndexManager
    {
        private readonly IVersionedDocumentStore<HostSnapshot> _store;

        public HostIndexManager(CloudBlobClient client)
            : this(VersionedDocumentStore.CreateJsonBlobStore<HostSnapshot>(
                client, DashboardContainerNames.Dashboard, DashboardDirectoryNames.Hosts))
        {
        }

        private HostIndexManager(IVersionedDocumentStore<HostSnapshot> store)
        {
            _store = store;
        }

        public bool UpdateOrCreateIfLatest(string id, HostSnapshot snapshot)
        {
            return _store.UpdateOrCreateIfLatest(id, snapshot.HostVersion, otherMetadata: null, document: snapshot);
        }
    }
}
