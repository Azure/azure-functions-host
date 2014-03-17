namespace Microsoft.WindowsAzure.Jobs
{
    internal interface IFunctionInstanceLogger
    {
        void LogFunctionStarted(ExecutionInstanceLogEntity logEntity);

        void LogFunctionCompleted(ExecutionInstanceLogEntity logEntity);
    }
}
