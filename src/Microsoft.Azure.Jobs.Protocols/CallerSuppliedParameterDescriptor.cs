#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a parameter bound to a caller-supplied value.</summary>
    [JsonTypeName("CallerSupplied")]
#if PUBLICPROTOCOL
    public class CallerSuppliedParameterDescriptor : ParameterDescriptor
#else
    internal class CallerSuppliedParameterDescriptor : ParameterDescriptor
#endif
    {
    }
}
