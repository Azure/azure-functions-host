using Microsoft.Azure.Jobs.Host.Executors;

namespace Microsoft.Azure.Jobs.Host.Loggers
{
    internal interface IExecutionLogger
    {
        FunctionExecutionContext GetExecutionContext();
    }    
}
