using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.Azure.Jobs
{
    /// <summary>
    /// Define the kind of trigger
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    internal enum TriggerType
    {
        /// <summary>
        /// Blob trigger, invoked when an input blob is detected. 
        /// </summary>
        Blob = 1,

        /// <summary>
        /// Service Bus trigger, on a message in a subscription or queue.
        /// </summary>
        ServiceBus = 3
    }
}
