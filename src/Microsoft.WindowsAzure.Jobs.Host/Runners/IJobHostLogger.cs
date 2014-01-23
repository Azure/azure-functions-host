namespace Microsoft.WindowsAzure.Jobs.Internals
{
    // Callback interface to let host log things to the console. 
    interface IJobHostLogger
    {
        void LogFunctionStart(FunctionInvokeRequest request);
        void LogFunctionEnd(ExecutionInstanceLogEntity logItem);
    }
}
