using Microsoft.Azure.Jobs.Protocols;

namespace Dashboard.Data
{
    public interface IFunctionQueuedLogger
    {
        // Using FunctionStartedSnapshot is a slight abuse; StartTime here is really QueueTime.
        void LogFunctionQueued(FunctionStartedMessage message);
    }
}
