using System.Threading;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    // Callback interface for invoking triggers.
    internal interface ITriggerInvoke
    {
        void OnNewQueueItem(CloudQueueMessage msg, QueueTrigger func, CancellationToken token);

        void OnNewBlob(CloudBlob blob, BlobTrigger func, CancellationToken token);
    }
}
