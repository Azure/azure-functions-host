using System;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a message from a host instance.</summary>
#if PUBLICPROTOCOL
    public abstract class HostOutputMessage : PersistentQueueMessage
#else
    internal abstract class HostOutputMessage : PersistentQueueMessage
#endif
    {
        /// <summary>Gets or sets the host instance ID.</summary>
        public Guid HostInstanceId { get; set; }

        /// <summary>Gets or sets a short, non-unique name for the host suitable for display purposes.</summary>
        public string HostDisplayName { get; set; }

        /// <summary>
        /// Gets or sets the name of the shared queue to which all instances of this host listen, if any.
        /// </summary>
        public string SharedQueueName { get; set; }

        /// <summary>Gets or sets the heartbeat for the host instance, if any.</summary>
        public HeartbeatDescriptor Heartbeat { get; set; }

        /// <summary>Gets or sets the connection string for Azure Storage data.</summary>
        public string StorageConnectionString { get; set; }

        /// <summary>Gets or sets the connection string for Service Bus data.</summary>
        public string ServiceBusConnectionString { get; set; }

        /// <summary>Gets or sets the ID of the web job under which the function is running, if any.</summary>
        public WebJobRunIdentifier WebJobRunIdentifier { get; set; }
    }
}
