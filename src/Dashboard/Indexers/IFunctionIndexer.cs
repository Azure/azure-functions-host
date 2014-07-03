using Microsoft.Azure.Jobs.Protocols;

namespace Dashboard.Indexers
{
    public interface IFunctionIndexer
    {
        void ProcessFunctionStarted(FunctionStartedMessage message);

        void ProcessFunctionCompleted(FunctionCompletedMessage message);
    }
}
