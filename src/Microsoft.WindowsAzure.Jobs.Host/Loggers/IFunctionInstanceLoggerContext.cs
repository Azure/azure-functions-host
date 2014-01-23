namespace Microsoft.WindowsAzure.Jobs
{
    internal interface IFunctionInstanceLoggerContext
    {
        void IndexRunningFunction();

        void IndexCompletedFunction();

        void Flush();
    }
}
