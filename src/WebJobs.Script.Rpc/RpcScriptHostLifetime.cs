using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Host;
using Microsoft.Azure.WebJobs.Script.Workers;

namespace Microsoft.Azure.WebJobs.Script.Rpc;

internal class RpcScriptHostLifetime : IScriptHostLifetime
{
    private readonly IFunctionInvocationDispatcher _dispatcher;

    public RpcScriptHostLifetime(IFunctionInvocationDispatcherFactory dispatcherFactory)
    {
        _dispatcher = dispatcherFactory.GetFunctionDispatcher();
    }

    public Task InitializedAsync(IEnumerable<FunctionMetadata> functions, CancellationToken cancellationToken)
    {
        return _dispatcher.InitializeAsync(functions, cancellationToken);
    }
}
