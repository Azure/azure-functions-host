using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class NewBlobRuntimeBindingInputs : RuntimeBindingInputs, ITriggerNewBlob
    {
        public NewBlobRuntimeBindingInputs(FunctionLocation location, ICloudBlob blobInput)
            : base(location)
        {
            this.BlobInput = blobInput;
        }

        // The blob that triggered this input
        public ICloudBlob BlobInput { get; private set; }
    }
}
