using System.IO;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a parameter bound to a queue in Azure Storage.</summary>
    [JsonTypeName("Queue")]
#if PUBLICPROTOCOL
    public class QueueParameterDescriptor : ParameterDescriptor
#else
    internal class QueueParameterDescriptor : ParameterDescriptor
#endif
    {
        /// <summary>Gets or sets the name of the queue.</summary>
        public string QueueName { get; set; }

        /// <summary>Gets or sets the kind of access the parameter has to the queue.</summary>
        public FileAccess Access { get; set; }
    }
}
