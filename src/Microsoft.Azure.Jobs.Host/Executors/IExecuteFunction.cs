using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    // Execute a function as well as updating all associated logging. 
    internal interface IExecuteFunction
    {
        FunctionInvocationResult Execute(IFunctionInstance instance, HostBindingContext context);
    }
}
