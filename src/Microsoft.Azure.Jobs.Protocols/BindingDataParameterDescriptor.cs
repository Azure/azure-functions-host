#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a parameter bound to binding data.</summary>
    [JsonTypeName("BindingData")]
#if PUBLICPROTOCOL
    public class BindingDataParameterDescriptor : ParameterDescriptor
#else
    internal class BindingDataParameterDescriptor : ParameterDescriptor
#endif
    {
    }
}
