using System;

namespace Microsoft.Azure.Jobs.Host.Protocols
{
    internal class NullFunctionsInJobIndexer : IFunctionsInJobIndexer
    {
        public void RecordFunctionInvocationForJobRun(Guid invocationId, DateTime startTime)
        {
        }
    }
}