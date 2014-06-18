using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public static class VersionedTextStore
    {
        [CLSCompliant(false)]
        public static IVersionedTextStore CreateBlobStore(CloudBlobClient client, string containerName)
        {
            CloudBlobContainer container = client.GetContainerReference(containerName);
            return new BlobVersionedTextStore(container);
        }
    }
}
