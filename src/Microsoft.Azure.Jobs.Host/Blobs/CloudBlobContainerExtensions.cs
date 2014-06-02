using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal static class CloudBlobContainerExtensions
    {
        public static ICloudBlob GetExistingOrNewBlockBlobReference(this CloudBlobContainer container, string blobName)
        {
            try
            {
                return container.GetBlobReferenceFromServer(blobName);
            }
            catch (StorageException exception)
            {
                RequestResult result = exception.RequestInformation;

                if (result == null || result.HttpStatusCode != 404)
                {
                    throw;
                }
                else
                {
                    return container.GetBlockBlobReference(blobName);
                }
            }
        }
    }
}
