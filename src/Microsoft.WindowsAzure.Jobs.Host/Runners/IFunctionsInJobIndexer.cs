using System;

namespace Microsoft.WindowsAzure.Jobs
{
    internal interface IFunctionsInJobIndexer
    {
        void RecordFunctionInvocationForJobRun(Guid invocationId, DateTime startTime);
    }
}