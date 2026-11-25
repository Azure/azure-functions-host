// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Workers;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Exposes ScriptHost worker status without owning externally managed worker processes.
/// </summary>
internal sealed class RpcClientScriptHostWorkerManager(
    IRpcClientFunctionInvocationDispatcher dispatcher,
    IWorkerChannelRegistry channelRegistry) : IScriptHostWorkerManager
{
    private readonly IRpcClientFunctionInvocationDispatcher _dispatcher =
        dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    private readonly IWorkerChannelRegistry _channelRegistry =
        channelRegistry ?? throw new ArgumentNullException(nameof(channelRegistry));

    public WorkerManagerState State => _dispatcher.State is FunctionInvocationDispatcherState.Default
        ? WorkerManagerState.Default
        : WorkerManagerState.Initialized;

    public Task GetWorkerStatusesAsync()
    {
        return _dispatcher.State is FunctionInvocationDispatcherState.Initialized
            ? _dispatcher.GetWorkerStatusesAsync()
            : Task.CompletedTask;
    }

    public Task<bool> RestartWorkerWithInvocationIdAsync(string invocationId, Exception exception)
        => _dispatcher.RestartWorkerWithInvocationIdAsync(invocationId, exception);

    public Task<IEnumerable<WorkerProcessInfo>> GetWorkerProcessInfoAsync(string workerRuntime)
        => Task.FromResult<IEnumerable<WorkerProcessInfo>>([]);

    public IReadOnlyList<WorkerRuntimeInfo> GetWorkerRuntimeInfo(string workerRuntime)
        => _channelRegistry.GetInitializedChannels()
            .Select(channel => channel.ReportedRuntimeInfo)
            .Where(info => info is not null)
            .Distinct()
            .ToList();
}
