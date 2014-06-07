using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a parameter bound to a table in Azure Storage.</summary>
    [JsonTypeName("Table")]
#if PUBLICPROTOCOL
    public class TableParameterDescriptor : ParameterDescriptor
#else
    internal class TableParameterDescriptor : ParameterDescriptor
#endif
    {
        /// <summary>Gets or sets the name of the storage account.</summary>
        public string AccountName { get; set; }

        /// <summary>Gets or sets the name of the table.</summary>
        public string TableName { get; set; }

        /// <summary>Gets or sets the kind of access the parameter has to the table.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public FileAccess Access { get; set; }
    }
}
