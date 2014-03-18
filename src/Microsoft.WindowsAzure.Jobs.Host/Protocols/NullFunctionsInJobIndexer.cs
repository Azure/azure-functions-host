using System;

namespace Microsoft.WindowsAzure.Jobs.Host.Protocols
{
    internal class NullFunctionsInJobIndexer : IFunctionsInJobIndexer
    {
        public void RecordFunctionInvocationForJobRun(Guid invocationId, DateTime startTime)
        {
        }
    }
}