// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Host;
using Microsoft.Azure.WebJobs.Script.Workers;

namespace Microsoft.Azure.WebJobs.Script.Rpc;

internal sealed class RpcScriptHostLifecycleService : IScriptHostLifecycleService
{
    private readonly IFunctionInvocationDispatcher _dispatcher;

    public RpcScriptHostLifecycleService(IFunctionInvocationDispatcherFactory dispatcherFactory)
    {
        _dispatcher = dispatcherFactory.GetFunctionDispatcher();
    }

    public Task InitializedAsync(IEnumerable<FunctionMetadata> functions, CancellationToken cancellationToken)
    {
        return _dispatcher.InitializeAsync(functions, cancellationToken);
    }

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        _dispatcher.PreShutdown();
        return Task.CompletedTask;
    }
}
