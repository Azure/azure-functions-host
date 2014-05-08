using System;
using Microsoft.Azure.Jobs;

namespace Dashboard.Data
{
    internal interface IFunctionsInJobIndexer
    {
        void RecordFunctionInvocationForJobRun(Guid invocationId, DateTime startTime, WebJobRunIdentifier webJobRunId);
    }
}