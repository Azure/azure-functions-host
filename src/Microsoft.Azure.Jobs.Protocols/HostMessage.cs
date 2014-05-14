using Newtonsoft.Json;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a message to any instance of a host.</summary>
    [JsonConverter(typeof(HostMessageConverter))]
#if PUBLICPROTOCOL
    public class HostMessage
#else
    internal class HostMessage
#endif
    {
        /// <summary>Gets or set the message type.</summary>
        public string Type { get; set; }

        private class HostMessageConverter : PolymorphicJsonConverter
        {
            public HostMessageConverter()
                : base("Type", PolymorphicJsonConverter.GetTypeMapping<HostMessage>())
            {
            }
        }
    }
}
