using System.Threading;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs
{
    // Callback interface for invoking triggers.
    internal interface ITriggerInvoke
    {
        void OnNewQueueItem(CloudQueueMessage msg, QueueTrigger func, RuntimeBindingProviderContext context);

        void OnNewBlob(ICloudBlob blob, BlobTrigger func, RuntimeBindingProviderContext context);
    }
}
