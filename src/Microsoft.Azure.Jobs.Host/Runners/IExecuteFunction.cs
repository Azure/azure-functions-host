using System.Threading;
namespace Microsoft.Azure.Jobs
{
    // Execute a function as well as updating all associated logging. 
    internal interface IExecuteFunction
    {
        ExecutionInstanceLogEntity Execute(FunctionInvokeRequest instance, CancellationToken cancellationToken);
    }
}
