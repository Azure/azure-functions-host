using System.Threading;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs
{
    // Callback interface for invoking triggers.
    internal interface ITriggerInvoke
    {
        void OnNewQueueItem(CloudQueueMessage msg, QueueTrigger func, CancellationToken token);

        void OnNewBlob(ICloudBlob blob, BlobTrigger func, CancellationToken token);
    }
}
