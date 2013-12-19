using System.Collections.Generic;
using System.Data.Services.Common;

namespace AzureTables
{
    // The DataServiceKey is needed to work with the Azure SDK's Table client. 
    [DataServiceKey("PartitionKey", "RowKey")]
    internal class GenericEntity
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public IDictionary<string, string> properties = new Dictionary<string, string>();
    }
}
