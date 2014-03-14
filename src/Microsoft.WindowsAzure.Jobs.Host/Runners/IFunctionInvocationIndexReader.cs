namespace Microsoft.WindowsAzure.Jobs
{
    internal interface IFunctionInvocationIndexReader
    {
        FunctionInvocationIndexEntity[] Query(string partitionKey, string olderThan, string olderThanOrEqual, string newerThan, int? limit);
    }
}