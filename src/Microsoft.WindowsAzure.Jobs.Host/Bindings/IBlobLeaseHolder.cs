using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.WindowsAzure.Jobs
{
    internal interface IBlobLeaseHolder : IDisposable
    {
        void BlockUntilAcquired(ICloudBlob blob);
        // release via Dipose()
        IBlobLeaseHolder TransferOwnership();

        void UploadText(string text);
    }
}
