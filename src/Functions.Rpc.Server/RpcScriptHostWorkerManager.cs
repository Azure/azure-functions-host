// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Rpc;

internal class RpcScriptHostWorkerManager : IScriptHostWorkerManager
{
    private readonly IFunctionInvocationDispatcher _dispatcher;
    private readonly IJobHostRpcWorkerChannelManager _jobHostManager;
    private readonly IWebHostRpcWorkerChannelManager _webHostManager;

    public RpcScriptHostWorkerManager(
        IFunctionInvocationDispatcherFactory dispatcherFactory,
        IJobHostRpcWorkerChannelManager jobHostManager,
        IWebHostRpcWorkerChannelManager webHostManager)
    {
        _dispatcher = dispatcherFactory.GetFunctionDispatcher();
        _jobHostManager = jobHostManager;
        _webHostManager = webHostManager;
    }

    public WorkerManagerState State =>
        _dispatcher.State switch
        {
            FunctionInvocationDispatcherState.Default => WorkerManagerState.Default,
            _ => WorkerManagerState.Initialized
        };

    public async Task<IEnumerable<WorkerProcessInfo>> GetWorkerProcessInfoAsync(string workerRuntime)
    {
        List<IRpcWorkerChannel> channels = _jobHostManager.GetChannels(workerRuntime).ToList();

        var webhostChannelDictionary = _webHostManager.GetChannels(workerRuntime);

        List<Task<IRpcWorkerChannel>> webHostchannelTasks = new List<Task<IRpcWorkerChannel>>();
        if (webhostChannelDictionary is not null)
        {
            foreach (var pair in webhostChannelDictionary)
            {
                var workerChannel = pair.Value.Task;
                webHostchannelTasks.Add(workerChannel);
            }
        }

        var webHostchannels = await Task.WhenAll(webHostchannelTasks);
        channels = channels ?? new List<IRpcWorkerChannel>();
        channels.AddRange(webHostchannels);

        var processes = new List<WorkerProcessInfo>();

        foreach (var channel in channels)
        {
            var processInfo = new WorkerProcessInfo()
            {
                ProcessId = channel.WorkerProcess.Process.Id,
                ProcessName = channel.WorkerProcess.Process.ProcessName,
                DebugEngine = Utility.GetDebugEngineInfo(channel.WorkerConfig, workerRuntime),
            };
            processes.Add(processInfo);
        }

        return processes;
    }

    public IReadOnlyList<WorkerRuntimeInfo> GetWorkerRuntimeInfo(string workerRuntime)
    {
        IEnumerable<IRpcWorkerChannel> channels = _jobHostManager.GetChannels();

        // Only completed WebHost channels are inspected; status reads must never wait for worker startup.
        var webHostChannels = _webHostManager.GetChannels(workerRuntime)?.Values
            .Select(tcs => tcs.Task)
            .Where(task => task.IsCompletedSuccessfully)
            .Select(task => task.Result);

        if (webHostChannels is not null)
        {
            channels = channels.Union(webHostChannels);
        }

        return channels
            .Select(channel => channel?.ReportedRuntimeInfo)
            .Where(info => info is not null)
            .Distinct()
            .ToList();
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

    public Task<bool> RestartWorkerWithInvocationIdAsync(string invocationId, Exception exception)
    {
        return _dispatcher.RestartWorkerWithInvocationIdAsync(invocationId, exception);
    }
}
