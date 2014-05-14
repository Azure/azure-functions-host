#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a parameter bound to a table in an Azure Storage.</summary>
    [JsonTypeName("Table")]
#if PUBLICPROTOCOL
    public class TableParameterDescriptor : ParameterDescriptor
#else
    internal class TableParameterDescriptor : ParameterDescriptor
#endif
    {
        /// <summary>Gets or sets the name of the table.</summary>
        public string TableName { get; set; }
    }
}
