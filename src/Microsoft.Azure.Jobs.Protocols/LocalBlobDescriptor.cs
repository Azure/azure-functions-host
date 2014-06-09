#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a blob in the same storage account as the entity that references it.</summary>
#if PUBLICPROTOCOL
    public class LocalBlobDescriptor
#else
    internal class LocalBlobDescriptor
#endif
    {
        /// <summary>Gets or sets the container name.</summary>
        public string ContainerName { get; set; }

        /// <summary>Gets or sets the blob name.</summary>
        public string BlobName { get; set; }
    }
}
