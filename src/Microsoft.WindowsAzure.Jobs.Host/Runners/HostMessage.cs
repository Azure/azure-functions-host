using Newtonsoft.Json;

namespace Microsoft.WindowsAzure.Jobs.Host.Runners
{
    [JsonConverter(typeof(HostMessageConverter))]
    internal class HostMessage
    {
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
