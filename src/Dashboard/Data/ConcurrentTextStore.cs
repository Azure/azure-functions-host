using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public static class ConcurrentTextStore
    {
        [CLSCompliant(false)]
        public static IConcurrentMetadataTextStore CreateBlobStore(CloudBlobClient client, string containerName,
            string directoryName)
        {
            CloudBlobContainer container = client.GetContainerReference(containerName);
            CloudBlobDirectory directory = container.GetDirectoryReference(directoryName);
            return new BlobConcurrentTextStore(directory);
        }
    }
}
