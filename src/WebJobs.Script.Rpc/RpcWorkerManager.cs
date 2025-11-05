// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Workers;

namespace Microsoft.Azure.WebJobs.Script.Rpc;

internal class RpcWorkerManager : IWorkerManager
{
    private readonly IFunctionInvocationDispatcher _dispatcher;

    public RpcWorkerManager(IFunctionInvocationDispatcherFactory dispatcherFactory)
    {
        _dispatcher = dispatcherFactory.GetFunctionDispatcher();
    }

    public Task GetWorkerStatusesAsync()
    {
        // This is only called from one place (HostPerformanceManager) and the original contract was that
        // GetWorkerStatusAsync() is not called if the dispatcher is not initialized. It appears that it is used
        // to populate latency history internally and the result is never used directly, so don't return it.
        if (_dispatcher.State != FunctionInvocationDispatcherState.Initialized)
        {
            return Task.CompletedTask;
        }

        return _dispatcher.GetWorkerStatusesAsync();
    }
}
