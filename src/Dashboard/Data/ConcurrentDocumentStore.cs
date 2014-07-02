using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public static class ConcurrentDocumentStore
    {
        [CLSCompliant(false)]
        public static IConcurrentMetadataDocumentStore<TDocument> CreateJsonBlobStore<TDocument>(CloudBlobClient client,
            string containerName, string directoryName)
        {
            IConcurrentMetadataTextStore innerStore = ConcurrentTextStore.CreateBlobStore(client, containerName, directoryName);
            return new JsonConcurrentDocumentStore<TDocument>(innerStore);
        }
    }
}
