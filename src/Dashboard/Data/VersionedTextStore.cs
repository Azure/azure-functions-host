using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public static class VersionedTextStore
    {
        [CLSCompliant(false)]
        public static IVersionedMetadataTextStore CreateBlobStore(CloudBlobClient client, string containerName,
            string directoryName, IVersionMetadataMapper versionMapper)
        {
            IConcurrentMetadataTextStore innerStore = ConcurrentTextStore.CreateBlobStore(client, containerName,
                directoryName);
            return new VersionedMetadataTextStore(innerStore, versionMapper);
        }
    }
}
