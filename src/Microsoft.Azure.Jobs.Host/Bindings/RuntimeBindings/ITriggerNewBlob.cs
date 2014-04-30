using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs
{
    // extension interface to IRuntimeBindingInputs, for when the input is triggered by a new blob
    internal interface ITriggerNewBlob : IRuntimeBindingInputs
    {
        // If null, then ignore.
        ICloudBlob BlobInput { get; }
    }
}
