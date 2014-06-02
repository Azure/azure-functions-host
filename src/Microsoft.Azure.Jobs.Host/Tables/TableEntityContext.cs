using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Tables
{
    internal class TableEntityContext
    {
        public CloudTable Table { get; set; }

        public string PartitionKey { get; set; }

        public string RowKey { get; set; }

        public string ToInvokeString()
        {
            return Table.Name + "/" + PartitionKey + "/" + RowKey;
        }
    }
}
