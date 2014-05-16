using Microsoft.Azure.Jobs.Host.Protocols;
using Newtonsoft.Json;

namespace Dashboard.Data
{
    [JsonConverter(typeof(ParameterSnapshotConverter))]
    public abstract class ParameterSnapshot
    {
        /// <summary>Gets or sets the parameter type.</summary>
        public string Type { get; set; }

        private class ParameterSnapshotConverter : PolymorphicJsonConverter
        {
            public ParameterSnapshotConverter()
                : base("Type", PolymorphicJsonConverter.GetTypeMapping<ParameterSnapshot>())
            {
            }
        }

        [JsonIgnore]
        public abstract string Description { get; }

        [JsonIgnore]
        public abstract string Prompt { get; }

        [JsonIgnore]
        public abstract string DefaultValue { get; }
    }
}
