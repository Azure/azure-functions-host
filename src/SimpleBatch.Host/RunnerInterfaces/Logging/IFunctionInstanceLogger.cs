namespace Microsoft.WindowsAzure.Jobs
{
    internal interface IFunctionInstanceLogger
    {
        IFunctionInstanceLoggerContext CreateContext(ExecutionInstanceLogEntity func);

        void Flush();
    }
}
