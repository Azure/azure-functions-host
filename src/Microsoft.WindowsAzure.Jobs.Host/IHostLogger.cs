namespace Microsoft.WindowsAzure.Jobs.Internals
{
    // Callback interface to let host log things to the console. 
    interface IHostLogger
    {
        void LogFunctionStart(FunctionInvokeRequest request);
        void LogFunctionEnd(ExecutionInstanceLogEntity logItem);
    }
}
