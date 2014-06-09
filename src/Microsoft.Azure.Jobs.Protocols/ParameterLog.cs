using Newtonsoft.Json;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a function parameter log.</summary>
    [JsonConverter(typeof(ParameterLogConverter))]
#if PUBLICPROTOCOL
    public class ParameterLog
#else
    internal class ParameterLog
#endif
    {
        /// <summary>Gets or sets the log type.</summary>
        public string Type { get; set; }

        private class ParameterLogConverter : PolymorphicJsonConverter
        {
            public ParameterLogConverter()
                : base("Type", PolymorphicJsonConverter.GetTypeMapping<ParameterLog>())
            {
            }
        }
    }
}
