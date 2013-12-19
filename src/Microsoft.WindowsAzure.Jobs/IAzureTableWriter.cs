namespace Microsoft.WindowsAzure.Jobs
{
    internal interface IAzureTableWriter
    {
        // Dictionary should have RowKey and PartitionKey
        // This is Upsert by default. 

        // if values is an IDictionary<string,string>, then use that.
        // Else if it's an object (including an anonymous type), convert it to an 
        // IDictionary based on the properties. 
        void Write(string partitionKey, string rowKey, object values);

        // Delete
        // Delete a specific partition and rowKey.
        // if RowKey is null, then delete the entire partition.
        // This blocks until deleted. 
        // No errors if entities does not.
        void Delete(string partitionKey, string rowKey = null);

        // Batching is a critical performance requirement for azure tables.
        void Flush();
    }
}
