using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public static class VersionedTextStore
    {
        [CLSCompliant(false)]
        public static IVersionedTextStore CreateBlobStore(CloudBlobClient client, string containerName,
            string directoryName)
        {
            CloudBlobContainer container = client.GetContainerReference(containerName);
            CloudBlobDirectory directory = container.GetDirectoryReference(directoryName);
            return new BlobVersionedTextStore(directory);
        }
    }
}
