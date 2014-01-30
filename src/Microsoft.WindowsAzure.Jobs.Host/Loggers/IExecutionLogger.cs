namespace Microsoft.WindowsAzure.Jobs
{
    internal interface IExecutionLogger
    {
        FunctionExecutionContext GetExecutionContext();
    }    
}
