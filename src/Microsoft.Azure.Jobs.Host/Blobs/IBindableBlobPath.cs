using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    interface IBindableBlobPath : IBindablePath<BlobPath>
    {
        string ContainerNamePattern { get; }
        string BlobNamePattern { get; }
    }
}
