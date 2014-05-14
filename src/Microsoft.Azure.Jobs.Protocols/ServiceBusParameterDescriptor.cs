#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>
    /// Represents a parameter bound to an Azure Service Bus entity.
    /// </summary>
    [JsonTypeName("ServiceBus")]
#if PUBLICPROTOCOL
    public class ServiceBusParameterDescriptor : ParameterDescriptor
#else
    internal class ServiceBusParameterDescriptor : ParameterDescriptor
#endif
    {
        /// <summary>Gets or sets the entity path.</summary>
        public string EntityPath { get; set; }

        /// <summary>Gets or sets a value indicating whether the parameter is an input parameter.</summary>
        public bool IsInput { get; set; }
    }
}
