#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>
    /// Represents a parameter bound to an Azure Service Bus entity.
    /// </summary>
    [JsonTypeName("ServiceBusTrigger")]
#if PUBLICPROTOCOL
    public class ServiceBusTriggerParameterDescriptor : ParameterDescriptor
#else
    internal class ServiceBusTriggerParameterDescriptor : ParameterDescriptor
#endif
    {
        /// <summary>Gets or sets the name of the Service Bus namespace.</summary>
        public string NamespaceName { get; set; }

        /// <summary>Gets or sets the name of the queue.</summary>
        /// <remarks>When binding to a subscription in a topic, returns <see langword="null"/>.</remarks>
        public string QueueName { get; set; }

        /// <summary>Gets or sets the name of the queue.</summary>
        /// <remarks>When binding to a queue, returns <see langword="null"/>.</remarks>
        public string TopicName { get; set; }

        /// <summary>Gets or sets the name of the subscription in <see cref="TopicName"/>.</summary>
        /// <remarks>When binding to a queue, returns <see langword="null"/>.</remarks>
        public string SubscriptionName { get; set; }
    }
}
