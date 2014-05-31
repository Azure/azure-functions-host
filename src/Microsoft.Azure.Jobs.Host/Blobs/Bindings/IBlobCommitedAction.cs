using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs.Bindings
{
    internal interface IBlobCommitedAction
    {
        void Execute();
    }
}
