namespace Microsoft.Azure.Jobs
{
    internal interface IFunctionInstanceLogger
    {
        void LogFunctionStarted(ExecutionInstanceLogEntity logEntity);

        void LogFunctionCompleted(ExecutionInstanceLogEntity logEntity);
    }
}
