using Microsoft.Azure.Jobs;
using Microsoft.Azure.Jobs.Protocols;

namespace Dashboard.Data
{
    internal interface IFunctionQueuedLogger
    {
        // Using FunctionStartedSnapshot is a slight abuse; StartTime here is really QueueTime.
        void LogFunctionQueued(FunctionStartedSnapshot snapshot);
    }
}
