using System;

#if PUBLICSTORAGE
namespace Microsoft.Azure.Jobs.Storage.Table
#else
namespace Microsoft.Azure.Jobs.Host.Storage.Table
#endif
{
    /// <summary>Defines a table client.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public interface ICloudTableClient
#else
    internal interface ICloudTableClient
#endif
    {
        /// <summary>Gets a table reference.</summary>
        /// <param name="tableName">The table name.</param>
        /// <returns>A table reference.</returns>
        ICloudTable GetTableReference(string tableName);
    }
}
