using System;
using System.Collections.Generic;
using System.Data.Services.Common;

namespace AzureTables
{
    // The DataServiceKey is needed to work with the Azure SDK's Table client. 
    [DataServiceKey("PartitionKey", "RowKey")]
#pragma warning disable 3019 // The compiler is unhappy both with and without CLSCompliant(false) on this internal type.
    [CLSCompliant(false)]
#pragma warning restore 3019
    internal class GenericEntity
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public IDictionary<string, string> properties = new Dictionary<string, string>();
    }
}
