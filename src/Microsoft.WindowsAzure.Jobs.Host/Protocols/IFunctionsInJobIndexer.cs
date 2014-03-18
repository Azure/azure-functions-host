using System;

namespace Microsoft.WindowsAzure.Jobs.Host.Protocols
{
    internal interface IFunctionsInJobIndexer
    {
        void RecordFunctionInvocationForJobRun(Guid invocationId, DateTime startTime);
    }
}