using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Dashboard.ViewModels
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum WebJobTypes
    {
        Triggered,
        Continuous
    }
}
