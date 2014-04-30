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
        /// Queue Trigger, invoked when a new queue mesasge is detected
        /// </summary>
        Queue = 2
    }
}
