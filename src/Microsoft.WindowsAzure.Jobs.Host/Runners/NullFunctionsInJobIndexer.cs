using System;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class NullFunctionsInJobIndexer : IFunctionsInJobIndexer
    {
        public void RecordFunctionInvocationForJobRun(Guid invocationId, DateTime startTime)
        {
        }
    }
}