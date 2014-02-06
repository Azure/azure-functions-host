using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.WindowsAzure.Jobs
{
    [JsonConverter(typeof(StringEnumConverter))]
    internal enum InvocationMessageType
    {
        TriggerAndOverride
    }
}
