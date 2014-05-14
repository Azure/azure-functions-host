#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a parameter bound to a queue in an Azure Storage.</summary>
    [JsonTypeName("Queue")]
#if PUBLICPROTOCOL
    public class QueueParameterDescriptor : ParameterDescriptor
#else
    internal class QueueParameterDescriptor : ParameterDescriptor
#endif
    {
        /// <summary>Gets or sets the name of the queue.</summary>
        public string QueueName { get; set; }

        /// <summary>Gets or sets a value indicating whether the parameter is an input parameter.</summary>
        public bool IsInput { get; set; }
    }
}
