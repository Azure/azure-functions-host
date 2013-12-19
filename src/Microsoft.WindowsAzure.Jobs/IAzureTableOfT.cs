namespace Microsoft.WindowsAzure.Jobs
{
    internal interface IAzureTable<T> : IAzureTableReader<T>, IAzureTableWriter
    {
    }
}
