using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    // extension interface to IRuntimeBindingInputs, for when the input is triggered by a new blob
    internal interface ITriggerNewBlob : IRuntimeBindingInputs
    {
        // If null, then ignore.
        CloudBlob BlobInput { get; }
    }
}
