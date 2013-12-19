using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class NewBlobRuntimeBindingInputs : RuntimeBindingInputs, ITriggerNewBlob
    {
        public NewBlobRuntimeBindingInputs(FunctionLocation location, CloudBlob blobInput)
            : base(location)
        {
            this.BlobInput = blobInput;
        }

        // The blob that triggered this input
        public CloudBlob BlobInput { get; private set; }
    }
}
