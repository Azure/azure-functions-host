#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a parameter bound to an Azure Storage account.</summary>
    [JsonTypeName("CloudStorageAccount")]
#if PUBLICPROTOCOL
    public class CloudStorageAccountParameterDescriptor : ParameterDescriptor
#else
    internal class CloudStorageAccountParameterDescriptor : ParameterDescriptor
#endif
    {
    }
}
