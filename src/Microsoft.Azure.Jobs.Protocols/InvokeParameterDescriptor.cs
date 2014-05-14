#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a parameter bound to a caller-supplied value.</summary>
    [JsonTypeName("Invoke")]
#if PUBLICPROTOCOL
    public class InvokeParameterDescriptor : ParameterDescriptor
#else
    internal class InvokeParameterDescriptor : ParameterDescriptor
#endif
    {
    }
}
