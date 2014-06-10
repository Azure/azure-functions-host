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
        private const string HostIdKeyName = "HostId";

        /// <summary>Gets or sets the functions the host instance contains.</summary>
        public IEnumerable<FunctionDescriptor> Functions { get; set; }

        internal override void AddMetadata(IDictionary<string, string> metadata)
        {
            metadata.Add(MessageTypeKeyName, "HostStarted");
            metadata.Add(HostIdKeyName, SharedQueueName);
        }
    }
}
