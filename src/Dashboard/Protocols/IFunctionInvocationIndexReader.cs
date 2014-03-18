using Microsoft.WindowsAzure.Jobs.Host.Protocols;

namespace Dashboard.Protocols
{
    internal interface IFunctionInvocationIndexReader
    {
        FunctionInvocationIndexEntity[] Query(string partitionKey, string olderThan, string olderThanOrEqual, string newerThan, int? limit);
    }
}