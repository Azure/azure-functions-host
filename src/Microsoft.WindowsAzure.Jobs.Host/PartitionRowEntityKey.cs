using System;
using System.Data.Services.Common;

namespace Microsoft.WindowsAzure.Jobs
{
    // Delete still needs an entity, but just the partition and row keys. 
    [DataServiceKey("PartitionKey", "RowKey")]
#pragma warning disable 3019 // The compiler is unhappy both with and without CLSCompliant(false) on this internal type.
    [CLSCompliant(false)]
#pragma warning restore 3019
    internal class PartitionRowKeyEntity
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
    }
}
