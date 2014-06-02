namespace Microsoft.Azure.Jobs
{
    internal interface IAzureTable<T> : IAzureTableReader<T>, IAzureTableWriter
    {
    }
}
