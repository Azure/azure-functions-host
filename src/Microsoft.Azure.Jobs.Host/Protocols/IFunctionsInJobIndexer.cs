using System;

namespace Microsoft.Azure.Jobs.Host.Protocols
{
    internal interface IFunctionsInJobIndexer
    {
        void RecordFunctionInvocationForJobRun(Guid invocationId, DateTime startTime);
    }
}