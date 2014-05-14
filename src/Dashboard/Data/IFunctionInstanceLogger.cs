using Microsoft.Azure.Jobs.Protocols;

namespace Dashboard.Data
{
    internal interface IFunctionInstanceLogger
    {
        void LogFunctionStarted(FunctionStartedSnapshot snapshot);

        void LogFunctionCompleted(FunctionCompletedSnapshot snapshot);
    }
}
