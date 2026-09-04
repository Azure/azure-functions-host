// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Azure.WebJobs.Host.Executors.Internal;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Dispatches function invocations through client-backed worker channels.
/// </summary>
/// <remarks>
/// One dispatcher belongs to a ScriptHost child container. It borrows root-owned channels and never disposes them.
/// </remarks>
internal sealed partial class RpcClientFunctionInvocationDispatcher : IRpcClientFunctionInvocationDispatcher
{
    private static readonly TimeSpan DefaultChannelWaitTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(10);

    private readonly IWorkerChannelRegistry _channelRegistry;
    private readonly CancellationTokenSource _dispatcherStoppedSource = new();
    private readonly ILogger<RpcClientFunctionInvocationDispatcher> _logger;
    private readonly ManagedDependencyOptions _managedDependencyOptions;
    private readonly TimeSpan _channelWaitTimeout;
    private readonly Lock _stateLock = new();
    private readonly ScriptJobHostOptions _scriptHostOptions;
    private IReadOnlyList<FunctionMetadata> _functions = [];
    private int _nextChannelIndex = -1;
    private bool _disposed;
    private bool _stopping;

    public RpcClientFunctionInvocationDispatcher(
        IWorkerChannelRegistry channelRegistry,
        IOptions<ScriptJobHostOptions> scriptHostOptions,
        IOptions<ManagedDependencyOptions> managedDependencyOptions,
        ILogger<RpcClientFunctionInvocationDispatcher> logger)
        : this(channelRegistry, scriptHostOptions, managedDependencyOptions, logger, DefaultChannelWaitTimeout)
    {
    }

    internal RpcClientFunctionInvocationDispatcher(
        IWorkerChannelRegistry channelRegistry,
        IOptions<ScriptJobHostOptions> scriptHostOptions,
        IOptions<ManagedDependencyOptions> managedDependencyOptions,
        ILogger<RpcClientFunctionInvocationDispatcher> logger,
        TimeSpan channelWaitTimeout)
    {
        _channelRegistry = channelRegistry ?? throw new ArgumentNullException(nameof(channelRegistry));
        _scriptHostOptions = scriptHostOptions?.Value ?? throw new ArgumentNullException(nameof(scriptHostOptions));
        _managedDependencyOptions = managedDependencyOptions?.Value ?? throw new ArgumentNullException(nameof(managedDependencyOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (channelWaitTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(channelWaitTimeout), "The channel wait timeout must be greater than zero.");
        }

        _channelWaitTimeout = channelWaitTimeout;
    }

    public FunctionInvocationDispatcherState State { get; private set; }

    public int ErrorEventsThreshold => 3;

    public Task InvokeAsync(ScriptInvocationContext invocationContext)
    {
        ArgumentNullException.ThrowIfNull(invocationContext);

        // Keep the common path synchronous and allocation-free after the registry snapshot.
        WorkerChannel channel = GetReadyChannel();
        return channel is null
            ? InvokeWhenChannelIsReadyAsync(invocationContext)
            : DispatchInvocation(invocationContext, channel);
    }

    public Task SetupChannelAsync(WorkerChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_stopping)
        {
            throw new InvalidOperationException("The invocation dispatcher is stopping.");
        }

        if (State is FunctionInvocationDispatcherState.Default)
        {
            throw new InvalidOperationException("The invocation dispatcher has not been initialized.");
        }

        // Invocation buffers accept work while the worker completes its function-load responses.
        channel.SetupFunctionInvocationBuffers(_functions);
        channel.SendFunctionLoadRequests(_managedDependencyOptions, _scriptHostOptions.FunctionTimeout);
        return channel.InvocationBuffersInitialization;
    }

    private Task DispatchInvocation(ScriptInvocationContext invocationContext, WorkerChannel channel)
    {
        using FunctionInvoker.Scope scope = FunctionInvoker.BeginSystemScope();
        string functionId = invocationContext.FunctionMetadata.GetFunctionId();
        if (channel.FunctionInputBuffers.TryGetValue(functionId, out BufferBlock<ScriptInvocationContext> inputBuffer))
        {
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                Log.PostingInvocation(_logger, invocationContext.ExecutionContext.InvocationId, channel.Id);
            }

            inputBuffer.Post(invocationContext);
            return Task.CompletedTask;
        }

        throw new InvalidOperationException(
            $"Function '{invocationContext.FunctionMetadata.Name}' is not loaded by worker '{channel.Id}'.");
    }

    public async Task InitializeAsync(IEnumerable<FunctionMetadata> functions, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FunctionMetadata[] functionArray = functions?.ToArray() ?? [];

        if (functionArray.Length == 0)
        {
            Log.NoFunctions(_logger, nameof(RpcClientFunctionInvocationDispatcher));
            return;
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_stopping)
        {
            throw new InvalidOperationException("The invocation dispatcher is stopping.");
        }

        State = FunctionInvocationDispatcherState.Initializing;
        _functions = functionArray;

        // Metadata is available before this lifecycle callback, so eagerly load every worker linked during startup.
        IReadOnlyList<WorkerChannel> channels = _channelRegistry.GetInitializedChannels();
        List<Task> channelSetupTasks = [];
        foreach (WorkerChannel channel in channels)
        {
            try
            {
                channelSetupTasks.Add(SetupChannelAsync(channel));
            }
            catch (Exception exception)
            {
                Log.ChannelSetupFailed(_logger, exception, channel.Id);
                channelSetupTasks.Add(Task.FromException(exception));
            }
        }

        try
        {
            await Task.WhenAll(channelSetupTasks).WaitAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested && channels.Any(IsReadyForInvocations))
        {
            // A failed channel must not prevent startup when another linked channel can serve invocations.
        }

        AddLogUserCategory(functionArray);
        State = FunctionInvocationDispatcherState.Initialized;
    }

    public async Task<IDictionary<string, WorkerStatus>> GetWorkerStatusesAsync()
    {
        // Preserve snapshot order so each status remains paired with the channel that produced it.
        IReadOnlyList<WorkerChannel> channels = _channelRegistry.GetInitializedChannels();
        WorkerStatus[] statuses = await Task.WhenAll(channels.Select(channel => channel.GetWorkerStatusAsync()));

        Dictionary<string, WorkerStatus> result = new(StringComparer.Ordinal);
        for (int i = 0; i < channels.Count; i++)
        {
            result.Add(channels[i].Id, statuses[i]);
        }

        return result;
    }

    public async Task ShutdownAsync()
    {
        PreShutdown();

        // The registry owns channel disposal; the dispatcher only gives accepted invocations time to drain.
        Task[] drainTasks = [.. _channelRegistry.GetInitializedChannels()
            .Where(channel => channel.IsChannelReadyForInvocations())
            .Select(channel => channel.DrainInvocationsAsync())];

        try
        {
            await Task.WhenAll(drainTasks).WaitAsync(DefaultShutdownTimeout);
        }
        catch (TimeoutException)
        {
            Log.DrainTimedOut(_logger);
        }
    }

    public Task<bool> RestartWorkerWithInvocationIdAsync(string invocationId, Exception exception)
        => Task.FromResult(false);

    public Task StartWorkerChannel() => Task.CompletedTask;

    public void PreShutdown()
    {
        bool stop;
        lock (_stateLock)
        {
            // This latch is best effort. A ready-channel invocation racing shutdown may still be accepted and drained.
            stop = !_disposed && !_stopping;
            if (stop)
            {
                _stopping = true;
                State = FunctionInvocationDispatcherState.Disposing;
            }
        }

        if (stop)
        {
            _dispatcherStoppedSource.Cancel();
        }
    }

    public void Dispose()
    {
        bool dispose;
        lock (_stateLock)
        {
            dispose = !_disposed;
            if (dispose)
            {
                _disposed = true;
                _stopping = true;
                State = FunctionInvocationDispatcherState.Disposed;
            }
        }

        if (dispose)
        {
            _dispatcherStoppedSource.Cancel();
        }
    }

    private WorkerChannel GetReadyChannel()
    {
        IReadOnlyList<WorkerChannel> channels = _channelRegistry.GetInitializedChannels();
        if (channels.Count == 0)
        {
            return null;
        }

        if (channels.Count == 1)
        {
            // The common one-worker path avoids round-robin synchronization and scanning.
            WorkerChannel channel = channels[0];
            return IsReadyForInvocations(channel) ? channel : null;
        }

        // Start each scan at the next channel to spread work without allocating or locking.
        int startIndex = (int)((uint)Interlocked.Increment(ref _nextChannelIndex) % (uint)channels.Count);
        for (int offset = 0; offset < channels.Count; offset++)
        {
            WorkerChannel channel = channels[(startIndex + offset) % channels.Count];
            if (IsReadyForInvocations(channel))
            {
                return channel;
            }
        }

        return null;
    }

    private async Task InvokeWhenChannelIsReadyAsync(ScriptInvocationContext invocationContext)
    {
        // Lifecycle checks and cancellation linking stay off the ready-channel hot path.
        if (_stopping)
        {
            throw new InvalidOperationException("The invocation dispatcher is stopping.");
        }

        if (State is not FunctionInvocationDispatcherState.Initialized)
        {
            throw new InvalidOperationException("The invocation dispatcher has not been initialized.");
        }

        using CancellationTokenSource operationSource =
            CancellationTokenSource.CreateLinkedTokenSource(invocationContext.CancellationToken, _dispatcherStoppedSource.Token);
        operationSource.CancelAfter(_channelWaitTimeout);
        try
        {
            WorkerChannel initializedChannel = await _channelRegistry.WaitForFirstInitializedAsync(operationSource.Token)
                .WaitAsync(operationSource.Token);
            await initializedChannel.InvocationBuffersInitialization.WaitAsync(operationSource.Token);
        }
        catch (OperationCanceledException) when (!invocationContext.CancellationToken.IsCancellationRequested &&
            !_dispatcherStoppedSource.IsCancellationRequested)
        {
            throw new TimeoutException($"No client-backed worker channel became ready within {_channelWaitTimeout}.");
        }

        // Linking publishes an initialized channel; the coordinator must finish SetupChannelAsync before it is selectable.
        WorkerChannel channel = GetReadyChannel()
            ?? throw new InvalidOperationException("No client-backed worker channel is ready for invocations.");
        await DispatchInvocation(invocationContext, channel);
    }

    private static bool IsReadyForInvocations(WorkerChannel channel) => channel.IsChannelReadyForInvocations();

    private void AddLogUserCategory(IEnumerable<FunctionMetadata> functions)
    {
        foreach (FunctionMetadata metadata in functions)
        {
            metadata.Properties[LogConstants.CategoryNameKey] = LogCategories.CreateFunctionUserCategory(metadata.Name);
            metadata.Properties[ScriptConstants.LogPropertyHostInstanceIdKey] = _scriptHostOptions.InstanceId;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Trace, "Posting invocation {InvocationId} on worker {WorkerId}.")]
        public static partial void PostingInvocation(ILogger logger, Guid invocationId, string workerId);

        [LoggerMessage(1, LogLevel.Debug, "{Dispatcher} received no functions.")]
        public static partial void NoFunctions(ILogger logger, string dispatcher);

        [LoggerMessage(2, LogLevel.Debug, "Draining client-backed worker invocations timed out during dispatcher shutdown.")]
        public static partial void DrainTimedOut(ILogger logger);

        [LoggerMessage(3, LogLevel.Warning, "Failed to configure client-backed worker {WorkerId} for invocations.")]
        public static partial void ChannelSetupFailed(ILogger logger, Exception exception, string workerId);
    }
}
