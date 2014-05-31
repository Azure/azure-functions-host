using System.Threading;
using Microsoft.Azure.Jobs.Host.Runners;

namespace Microsoft.Azure.Jobs
{
    // Execute a function as well as updating all associated logging. 
    internal interface IExecuteFunction
    {
        FunctionInvocationResult Execute(FunctionInvokeRequest instance, INotifyNewBlob notifyNewBlob, CancellationToken cancellationToken);
    }
}
