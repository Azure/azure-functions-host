using System;
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
    public class HostStartedMessage : PersistentQueueMessage
#else
    internal class HostStartedMessage : PersistentQueueMessage
#endif
    {
        /// <summary>Gets or sets the host instance ID.</summary>
        public Guid HostInstanceId { get; set; }

        /// <summary>Gets or sets the host ID.</summary>
        public Guid HostId { get; set; }

        /// <summary>Gets or sets the connection string for Azure Storage data.</summary>
        public string StorageConnectionString { get; set; }

        /// <summary>Gets or sets the connection string for Service Bus data.</summary>
        public string ServiceBusConnectionString { get; set; }

        /// <summary>Gets or sets the functions the host instance contains.</summary>
        public IEnumerable<FunctionDescriptor> Functions { get; set; }
    }
}
