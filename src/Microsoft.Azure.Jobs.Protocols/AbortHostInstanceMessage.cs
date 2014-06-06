#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a request to abort the host instance.</summary>
    [JsonTypeName("Abort")]
#if PUBLICPROTOCOL
    public class AbortHostInstanceMessage : HostMessage
#else
    internal class AbortHostInstanceMessage : HostMessage
#endif
    {
    }
}
