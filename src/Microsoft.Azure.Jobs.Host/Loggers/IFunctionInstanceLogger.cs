using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Loggers
{
    internal interface IFunctionInstanceLogger
    {
        void LogFunctionStarted(FunctionStartedMessage message);

        void LogFunctionCompleted(FunctionCompletedMessage message);
    }
}
