#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a function parameter log for a table parameter.</summary>
    [JsonTypeName("Table")]
#if PUBLICPROTOCOL
    public class TableParameterLog : ParameterLog
#else
    internal class TableParameterLog : ParameterLog
#endif
    {
        /// <summary>Gets or sets the number of entities updated.</summary>
        public int EntitiesUpdated { get; set; }
    }
}
