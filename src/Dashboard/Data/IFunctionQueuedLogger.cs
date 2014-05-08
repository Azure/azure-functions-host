using Microsoft.Azure.Jobs;

namespace Dashboard.Data
{
    internal interface IFunctionQueuedLogger
    {
        void LogFunctionQueued(ExecutionInstanceLogEntity logEntity);
    }
}
