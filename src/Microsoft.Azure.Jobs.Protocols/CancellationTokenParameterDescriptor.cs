#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a parameter bound to a cancellation token.</summary>
    [JsonTypeName("CancellationToken")]
#if PUBLICPROTOCOL
    public class CancellationTokenParameterDescriptor : ParameterDescriptor
#else
    internal class CancellationTokenParameterDescriptor : ParameterDescriptor
#endif
    {
    }
}
