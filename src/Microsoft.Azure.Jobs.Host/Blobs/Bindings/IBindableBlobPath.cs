using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.Host.Blobs.Bindings
{
    interface IBindableBlobPath : IBindablePath<BlobPath>
    {
        string ContainerNamePattern { get; }
        string BlobNamePattern { get; }
    }
}
