#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents an Azure Storage account connection string.</summary>
    [JsonTypeName("Storage")]
#if PUBLICPROTOCOL
    public class StorageConnectionStringDescriptor : ConnectionStringDescriptor
#else
    internal class StorageConnectionStringDescriptor : ConnectionStringDescriptor
#endif
    {
        /// <summary>Gets or sets the account name of the connection string.</summary>
        public string Account { get; set; }

        /// <summary>Gets or sets the connection string value.</summary>
        public string ConnectionString { get; set; }
    }
}
