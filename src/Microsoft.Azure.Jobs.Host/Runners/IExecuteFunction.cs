using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Runners;

namespace Microsoft.Azure.Jobs
{
    // Execute a function as well as updating all associated logging. 
    internal interface IExecuteFunction
    {
        FunctionInvocationResult Execute(IFunctionInstance instance, RuntimeBindingProviderContext context);
    }
}
