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
        /// <summary>The name of the key used to store the message type in metadata.</summary>
        protected const string MessageTypeKeyName = "MessageType";

        /// <summary>Gets or sets the host instance ID.</summary>
        public Guid HostInstanceId { get; set; }

        /// <summary>Gets or sets a short, non-unique name for the host suitable for display purposes.</summary>
        public string HostDisplayName { get; set; }

        /// <summary>
        /// Gets or sets the name of the shared queue to which all instances of this host listen, if any.
        /// </summary>
        public string SharedQueueName { get; set; }

        /// <summary>
        /// Gets or sets the name of the shared queue to which only this host instance listens, if any.
        /// </summary>
        public string InstanceQueueName { get; set; }

        /// <summary>Gets or sets the heartbeat for the host instance, if any.</summary>
        public HeartbeatDescriptor Heartbeat { get; set; }

        /// <summary>Gets or sets the credentials used by the host instance.</summary>
        public CredentialsDescriptor Credentials { get; set; }

        /// <summary>Gets or sets the ID of the web job under which the function is running, if any.</summary>
        public WebJobRunIdentifier WebJobRunIdentifier { get; set; }
    }
}
