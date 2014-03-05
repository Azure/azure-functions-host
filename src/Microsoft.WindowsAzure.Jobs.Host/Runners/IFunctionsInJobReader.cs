namespace Microsoft.WindowsAzure.Jobs
{
    internal interface IFunctionsInJobReader
    {
        FunctionInJobEntity[] GetFunctionInvocationsForJobRun(WebJobRunIdentifier jobId, string olderThan, string newerThan, int? limit);
    }
}