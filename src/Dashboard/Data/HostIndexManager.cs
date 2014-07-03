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

        public void UpdateOrCreateIfLatest(string id, HostSnapshot snapshot)
        {
            _store.UpdateOrCreateIfLatest(id, snapshot.HostVersion, otherMetadata: null, document: snapshot);
        }
    }
}
