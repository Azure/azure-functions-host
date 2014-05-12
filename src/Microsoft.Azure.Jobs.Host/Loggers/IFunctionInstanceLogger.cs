using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs
{
    internal interface IFunctionInstanceLogger
    {
        void LogFunctionStarted(FunctionStartedSnapshot snapshot);

        void LogFunctionCompleted(FunctionCompletedSnapshot snapshot);
    }
}
