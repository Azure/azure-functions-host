#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a parameter bound to a route value.</summary>
    [JsonTypeName("Route")]
#if PUBLICPROTOCOL
    public class RouteParameterDescriptor : ParameterDescriptor
#else
    internal class RouteParameterDescriptor : ParameterDescriptor
#endif
    {
    }
}
