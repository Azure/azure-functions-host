#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a parameter bound to a blob in an Azure Storage.</summary>
    [JsonTypeName("Blob")]
#if PUBLICPROTOCOL
    public class BlobParameterDescriptor : ParameterDescriptor
#else
    internal class BlobParameterDescriptor : ParameterDescriptor
#endif
    {
        /// <summary>Gets or sets the name of the container.</summary>
        public string ContainerName { get; set; }

        /// <summary>Gets or sets the name of the blob.</summary>
        public string BlobName { get; set; }

        /// <summary>Gets or sets a value indicating whether the parameter is an input parameter.</summary>
        public bool IsInput { get; set; }
    }
}
