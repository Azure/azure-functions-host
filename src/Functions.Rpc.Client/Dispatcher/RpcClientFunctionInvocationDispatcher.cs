// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Metrics;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Dispatches function invocations through client-backed worker channels.
/// </summary>
/// <remarks>
/// One dispatcher belongs to a ScriptHost child container and owns its invocation scheduler.
/// Channels remain root-owned; the scheduler admits work without owning worker execution or response completion.
/// </remarks>
internal sealed partial class RpcClientFunctionInvocationDispatcher : IRpcClientFunctionInvocationDispatcher
{
    private static readonly TimeSpan DefaultChannelWaitTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(10);

    private readonly IWorkerChannelRegistry _channelRegistry;
    private readonly ILogger<RpcClientFunctionInvocationDispatcher> _logger;
    private readonly RpcClientInvocationScheduler _scheduler;
    private bool _disposed;
    private bool _stopping;

    public RpcClientFunctionInvocationDispatcher(
        IWorkerChannelRegistry channelRegistry,
        ILogger<RpcClientFunctionInvocationDispatcher> logger,
        IWorkerInvocationTracker invocationTracker = null)
        : this(channelRegistry, logger, DefaultChannelWaitTimeout, invocationTracker)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RpcClientFunctionInvocationDispatcher"/> class for a ScriptHost with
    /// an explicit matching-channel wait timeout.
    /// </summary>
    internal RpcClientFunctionInvocationDispatcher(
        IWorkerChannelRegistry channelRegistry,
        ILogger<RpcClientFunctionInvocationDispatcher> logger,
        TimeSpan channelWaitTimeout,
        IWorkerInvocationTracker invocationTracker = null)
    {
        _channelRegistry = channelRegistry ?? throw new ArgumentNullException(nameof(channelRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (channelWaitTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(channelWaitTimeout), "The channel wait timeout must be greater than zero.");
        }

        _scheduler = new(channelRegistry, logger, channelWaitTimeout, invocationTracker);
    }

    public FunctionInvocationDispatcherState State { get; private set; }

    public int ErrorEventsThreshold => 3;

    /// <summary>
    /// Admits an invocation to its matching worker group. HTTP traffic takes FIFO admission turns;
    /// execution and response completion are reported separately through the invocation's result source.
    /// </summary>
    public Task InvokeAsync(ScriptInvocationContext invocationContext)
    {
        ArgumentNullException.ThrowIfNull(invocationContext);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_stopping)
        {
            throw new InvalidOperationException("The invocation dispatcher is stopping.");
        }

        if (State is not FunctionInvocationDispatcherState.Initialized)
        {
            throw new InvalidOperationException("The invocation dispatcher has not been initialized.");
        }

        return _scheduler.InvokeAsync(invocationContext);
    }

    /// <summary>
    /// Initializes the local lifecycle; metadata enrichment and function loading remain owned by the ScriptHost lifecycle and root registry.
    /// </summary>
    public Task InitializeAsync(IEnumerable<FunctionMetadata> functions, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_stopping)
        {
            throw new InvalidOperationException("The invocation dispatcher is stopping.");
        }

        if (functions is null || !functions.Any())
        {
            Log.NoFunctions(_logger, nameof(RpcClientFunctionInvocationDispatcher));
            return Task.CompletedTask;
        }

        State = FunctionInvocationDispatcherState.Initializing;
        State = FunctionInvocationDispatcherState.Initialized;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Reads the Host-derived group captured during initialization without mutating shared metadata.
    /// </summary>
    internal string GetFunctionGroup(FunctionMetadata metadata) => RpcClientInvocationScheduler.GetFunctionGroup(metadata);

    /// <summary>
    /// Selects a matching ready channel using the scheduler's per-group cursor without reserving invocation capacity.
    /// </summary>
    internal bool TryGetReadyChannel(IReadOnlyList<WorkerChannel> channels, string functionGroup, [NotNullWhen(true)] out WorkerChannel channel)
        => _scheduler.TryGetReadyChannel(channels, functionGroup, out channel);

    /// <summary>
    /// Queries all initialized channels, including those not yet ready for invocation, and keys their status by worker ID.
    /// </summary>
    public async Task<IDictionary<string, WorkerStatus>> GetWorkerStatusesAsync()
    {
        IReadOnlyList<WorkerChannel> channels = _channelRegistry.GetInitializedChannels();
        WorkerStatus[] statuses = await Task.WhenAll(channels.Select(channel => channel.GetWorkerStatusAsync()));
        Dictionary<string, WorkerStatus> result = new(StringComparer.Ordinal);
        for (int i = 0; i < channels.Count; i++)
        {
            result.Add(channels[i].Id, statuses[i]);
        }

        return result;
    }

    /// <summary>
    /// Stops local admission and allows accepted invocations to drain within the shutdown timeout.
    /// Shared channels remain owned by the root registry and are not disposed here.
    /// </summary>
    public async Task ShutdownAsync()
    {
        PreShutdown();
        List<Task> drainTasks = [];
        foreach (WorkerChannel channel in _channelRegistry.GetInitializedChannels())
        {
            if (channel is RpcClientWorkerChannel { IsReadyForInvocations: true })
            {
                drainTasks.Add(channel.DrainInvocationsAsync());
            }
        }

        try
        {
            await Task.WhenAll(drainTasks).WaitAsync(DefaultShutdownTimeout);
        }
        catch (TimeoutException)
        {
            Log.DrainTimedOut(_logger);
        }
    }

    /// <summary>
    /// Declines worker restart requests because external worker lifetime is not owned by this dispatcher.
    /// </summary>
    public Task<bool> RestartWorkerWithInvocationIdAsync(string invocationId, Exception exception) => Task.FromResult(false);

    /// <summary>
    /// Leaves worker creation to the platform; externally linked channels are supplied by the registry.
    /// </summary>
    public Task StartWorkerChannel() => Task.CompletedTask;

    /// <summary>
    /// Rejects new dispatch and cancels queued admission and channel waits before accepted invocations are drained.
    /// </summary>
    public void PreShutdown()
    {
        if (_disposed || _stopping)
        {
            return;
        }

        _stopping = true;
        State = FunctionInvocationDispatcherState.Disposing;
        _scheduler.Stop();
    }

    /// <summary>
    /// Ends this dispatcher's lifecycle and disposes its scheduler without disposing root-owned channels.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopping = true;
        State = FunctionInvocationDispatcherState.Disposed;
        _scheduler.Dispose();
    }

    private static partial class Log
    {
        [LoggerMessage(1, LogLevel.Debug, "{Dispatcher} received no functions.")]
        public static partial void NoFunctions(ILogger logger, string dispatcher);

        [LoggerMessage(2, LogLevel.Debug, "Draining client-backed worker invocations timed out during dispatcher shutdown.")]
        public static partial void DrainTimedOut(ILogger logger);
    }
}
