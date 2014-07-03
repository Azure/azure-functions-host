using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public static class VersionedTextStore
    {
        [CLSCompliant(false)]
        public static IVersionedMetadataTextStore CreateBlobStore(CloudBlobClient client, string containerName,
            string directoryName)
        {
            IConcurrentMetadataTextStore innerStore = ConcurrentTextStore.CreateBlobStore(client, containerName,
                directoryName);
            IVersionMetadataMapper versionMapper = VersionMetadataMapper.Instance;
            return new VersionedMetadataTextStore(innerStore, versionMapper);
        }
    }
}
