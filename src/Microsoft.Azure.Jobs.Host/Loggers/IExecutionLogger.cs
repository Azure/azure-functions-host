namespace Microsoft.Azure.Jobs
{
    internal interface IExecutionLogger
    {
        FunctionExecutionContext GetExecutionContext();
    }    
}
