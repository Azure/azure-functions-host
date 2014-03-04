using System;
using Newtonsoft.Json;

namespace Microsoft.WindowsAzure.Jobs.Host.Protocols
{
    internal class PersistentQueueMessage
    {
        [JsonIgnore]
        public DateTime EnqueuedOn { get; set; }

        [JsonIgnore]
        public string PopReceipt { get; set; }
    }
}
