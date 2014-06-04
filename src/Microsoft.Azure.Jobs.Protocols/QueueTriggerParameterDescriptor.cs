#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a parameter triggered on a queue in Azure Storage.</summary>
    [JsonTypeName("QueueTrigger")]
#if PUBLICPROTOCOL
    public class QueueTriggerParameterDescriptor : ParameterDescriptor
#else
    internal class QueueTriggerParameterDescriptor : ParameterDescriptor
#endif
    {
        /// <summary>Gets or sets the name of the queue.</summary>
        public string QueueName { get; set; }
    }
}
