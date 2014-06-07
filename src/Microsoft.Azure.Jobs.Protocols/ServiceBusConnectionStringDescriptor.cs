#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents an Azure Service Bus account connection string.</summary>
    [JsonTypeName("ServiceBus")]
#if PUBLICPROTOCOL
    public class ServiceBusConnectionStringDescriptor : ConnectionStringDescriptor
#else
    internal class ServiceBusConnectionStringDescriptor : ConnectionStringDescriptor
#endif
    {
        /// <summary>Gets or sets the namespace of the connection string.</summary>
        public string Namespace { get; set; }

        /// <summary>Gets or sets the connection string value.</summary>
        public string ConnectionString { get; set; }
    }
}
