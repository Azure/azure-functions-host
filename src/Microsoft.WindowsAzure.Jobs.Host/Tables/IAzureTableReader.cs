using System.Collections.Generic;

namespace Microsoft.WindowsAzure.Jobs
{
    internal interface IAzureTableReader
    {
        // Lookup an azure entity by the partition and row key.
        // Returns null on missing. 
        // Result dictionary has all entity properties (including row key, partition key, and timestamp)
        IDictionary<string, string> Lookup(string partitionKey, string rowKey);

        // if partitionKey == null, enumerate all entities in the table.
        // else just enumerate that partition. 
        // Resulting dictionaries include the row key, partition key, and timestamp for each.
        IEnumerable<IDictionary<string, string>> Enumerate(string partitionKey = null);
    }
}
