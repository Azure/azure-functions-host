namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class TableBindingMetadata : BindingMetadata
    {
        public string TableName { get; set; }

        public string PartitionKey { get; set; }

        public string RowKey { get; set; }

        public int? Take { get; set; }

        public string Filter { get; set; }
    }
}
