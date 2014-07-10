using System.Collections.Generic;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a function parameter log for a runtime binder parameter.</summary>
    [JsonTypeName("IBinder")]
#if PUBLICPROTOCOL
    public class BinderParameterLog : ParameterLog
#else
    internal class BinderParameterLog : ParameterLog
#endif
    {
        /// <summary>Gets or sets the items bound.</summary>
        public IEnumerable<BinderParameterLogItem> Items { get; set; }
    }
}
