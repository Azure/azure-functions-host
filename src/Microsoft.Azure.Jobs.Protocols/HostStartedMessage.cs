using System.Collections.Generic;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a message indicating that a host instance started.</summary>
    [JsonTypeName("HostStarted")]
#if PUBLICPROTOCOL
    public class HostStartedMessage : HostOutputMessage
#else
    internal class HostStartedMessage : HostOutputMessage
#endif
    {
        /// <summary>Gets or sets the functions the host instance contains.</summary>
        public IEnumerable<FunctionDescriptor> Functions { get; set; }
    }
}
