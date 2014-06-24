using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs
{
    // Callback interface for invoking triggers.
    internal interface ITriggerInvoke
    {
        void OnNewBlob(ICloudBlob blob, BlobTrigger func, RuntimeBindingProviderContext context);
    }
}
