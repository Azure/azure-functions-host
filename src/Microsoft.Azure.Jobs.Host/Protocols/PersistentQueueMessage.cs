using System;
using Newtonsoft.Json;

namespace Microsoft.Azure.Jobs.Host.Protocols
{
    [JsonConverter(typeof(PersistentQueueMessageConverter))]
    internal class PersistentQueueMessage
    {
        public string Type { get; set; }

        [JsonIgnore]
        public DateTimeOffset EnqueuedOn { get; set; }

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
