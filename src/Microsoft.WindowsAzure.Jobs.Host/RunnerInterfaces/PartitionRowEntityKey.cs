using System.Data.Services.Common;

namespace Microsoft.WindowsAzure.Jobs
{
    // Delete still needs an entity, but just the partition and row keys. 
    [DataServiceKey("PartitionKey", "RowKey")]
    internal class PartitionRowKeyEntity
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
    }
}
