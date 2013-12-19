using System;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    internal interface IBlobLeaseHolder : IDisposable
    {
        void BlockUntilAcquired(CloudBlob blob);
        // release via Dipose()
        IBlobLeaseHolder TransferOwnership();

        void UploadText(string text);
    }
}
