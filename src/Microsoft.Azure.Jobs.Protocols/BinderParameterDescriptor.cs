#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a parameter bound to an IBinder.</summary>
    [JsonTypeName("IBinder")]
#if PUBLICPROTOCOL
    public class BinderParameterDescriptor : ParameterDescriptor
#else
    internal class BinderParameterDescriptor : ParameterDescriptor
#endif
    {
    }
}
