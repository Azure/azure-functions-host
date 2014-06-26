using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal interface IBlobWrittenWatcher
    {
        void Notify(ICloudBlob blobWritten);
    }
}
