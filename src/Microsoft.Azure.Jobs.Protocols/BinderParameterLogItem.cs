#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents an item bound using a runtime binder parameter.</summary>
#if PUBLICPROTOCOL
    public class BinderParameterLogItem
#else
    internal class BinderParameterLogItem
#endif
    {
        /// <summary>Gets or sets the parameter descriptor.</summary>
        public ParameterDescriptor Descriptor { get; set; }

        /// <summary>Gets or sets the parameter value.</summary>
        public string Value { get; set; }

        /// <summary>
        /// Gets or sets the parameter log.
        /// </summary>
        public ParameterLog Log { get; set; }
    }
}
