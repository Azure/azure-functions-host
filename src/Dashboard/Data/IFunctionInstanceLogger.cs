using Microsoft.Azure.Jobs.Protocols;

namespace Dashboard.Data
{
    internal interface IFunctionInstanceLogger
    {
        void LogFunctionStarted(FunctionStartedMessage message);

        void LogFunctionCompleted(FunctionCompletedMessage message);
    }
}
