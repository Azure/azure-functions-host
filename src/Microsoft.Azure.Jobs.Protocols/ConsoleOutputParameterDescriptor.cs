using System.IO;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a parameter bound to a <see cref="TextWriter"/> for console ouput.</summary>
    [JsonTypeName("ConsoleOutput")]
#if PUBLICPROTOCOL
    public class ConsoleOutputParameterDescriptor : ParameterDescriptor
#else
    internal class ConsoleOutputParameterDescriptor : ParameterDescriptor
#endif
    {
    }
}
