using System.Collections.Generic;

namespace Microsoft.Azure.Jobs
{
    // Like IAzureTableReader, but has strong binding instead of just IDictionary<>
    internal interface IAzureTableReader<T>
    {
        T Lookup(string partitionKey, string rowKey);

        // if partitionKey == null, enumerate all entities in the table.
        // else just enumerate that partition. 
        IEnumerable<T> Enumerate(string partitionKey = null);
    }
}
