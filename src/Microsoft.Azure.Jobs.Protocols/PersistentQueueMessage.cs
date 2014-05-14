using System;
using Newtonsoft.Json;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a message in a persistent queue.</summary>
    [JsonConverter(typeof(PersistentQueueMessageConverter))]
#if PUBLICPROTOCOL
    public class PersistentQueueMessage
#else
    internal class PersistentQueueMessage
#endif
    {
        /// <summary>Gets or sets the message type.</summary>
        public string Type { get; set; }

        /// <summary>Gets or sets the time the message was enqueued.</summary>
        [JsonIgnore]
        public DateTimeOffset EnqueuedOn { get; set; }

        /// <summary>Gets or sets a receipt from dequeuing the message.</summary>
        [JsonIgnore]
        public string PopReceipt { get; set; }

        private class PersistentQueueMessageConverter : PolymorphicJsonConverter
        {
            public PersistentQueueMessageConverter()
                : base("Type", PolymorphicJsonConverter.GetTypeMapping<PersistentQueueMessage>())
            {
            }
        }
    }
}
