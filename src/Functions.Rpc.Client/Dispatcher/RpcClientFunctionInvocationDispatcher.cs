// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors.Internal;
using Microsoft.Azure.WebJobs.Script;
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
/// One dispatcher belongs to a ScriptHost child container. It tracks only that ScriptHost's invocation lifecycle,
/// borrows root-owned channels already configured by the registry, and never disposes them.
/// </remarks>
internal sealed partial class RpcClientFunctionInvocationDispatcher : IRpcClientFunctionInvocationDispatcher
{
    private static readonly TimeSpan DefaultChannelWaitTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(10);

    private readonly IWorkerChannelRegistry _channelRegistry;
    private readonly IWorkerInvocationTracker _invocationTracker;
    private readonly CancellationTokenSource _dispatcherStoppedSource = new();
    private readonly ILogger<RpcClientFunctionInvocationDispatcher> _logger;
    private readonly TimeSpan _channelWaitTimeout;
    private readonly Dictionary<string, int> _nextChannelIndices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _selectionLock = new();
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
    /// Creates a ScriptHost-scoped dispatcher with an explicit matching-channel wait timeout.
    /// </summary>
    internal RpcClientFunctionInvocationDispatcher(
        IWorkerChannelRegistry channelRegistry,
        ILogger<RpcClientFunctionInvocationDispatcher> logger,
        TimeSpan channelWaitTimeout,
        IWorkerInvocationTracker invocationTracker = null)
    {
        _channelRegistry = channelRegistry ?? throw new ArgumentNullException(nameof(channelRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _invocationTracker = invocationTracker;

        if (channelWaitTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(channelWaitTimeout), "The channel wait timeout must be greater than zero.");
        }

        _channelWaitTimeout = channelWaitTimeout;
    }

    public FunctionInvocationDispatcherState State { get; private set; }

    public int ErrorEventsThreshold => 3;

    /// <summary>
    /// Invokes a function on a matching ready channel, waiting only when immediate selection fails.
    /// Execution completion is reported separately through the invocation's result source.
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

        string functionGroup = invocationContext.FunctionMetadata.GetFunctionGroup();
        // Capture invalidation before sampling so a matching worker arriving during selection cannot be missed.
        Task changed = _channelRegistry.GetChangeTask();
        ValueTask<IReadOnlyList<WorkerChannel>> readyChannels = _channelRegistry.GetReadyChannelsAsync();
        if (readyChannels.IsCompletedSuccessfully)
        {
            if (TryDispatchInvocation(invocationContext, readyChannels.Result, functionGroup))
            {
                return Task.CompletedTask;
            }
        }

        return InvokeWhenChannelIsReadyAsync(invocationContext, functionGroup, readyChannels, changed);
    }

    /// <summary>
    /// Atomically checks channel readiness and transfers invocation ownership before idle retirement can claim it.
    /// </summary>
    private bool DispatchInvocation(ScriptInvocationContext invocationContext, RpcClientWorkerChannel channel, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using FunctionInvoker.Scope scope = FunctionInvoker.BeginSystemScope();
        lock (channel.LifecycleLock)
        {
            if (!channel.IsReadyForInvocations)
            {
                return false;
            }

            bool trackActivity = channel.IsHttpFunctionGroup && channel.IsAlwaysReady is not true;
            // Register before posting so a fast response cannot outrun its completion callback.
            Task responseCompleted = trackActivity
                ? RpcClientWorkerChannel.GetHttpResponseCompletionTask(invocationContext)
                : Task.CompletedTask;

            string invocationId = invocationContext.ExecutionContext.InvocationId.ToString();
            bool accepted = _invocationTracker is null
                ? channel.TryPostInvocation(invocationContext)
                : _invocationTracker.TryDispatchInvocation(invocationId, channel.Id, invocationContext.CancellationToken,
                    () => channel.TryPostInvocation(invocationContext));
            if (!accepted)
            {
                invocationContext.ResultSource.TrySetException(
                    new InvalidOperationException($"Worker '{channel.Id}' is no longer accepting invocations or is unavailable for attribution."));
                return true;
            }

            if (_invocationTracker is not null)
            {
                // A cancellation request (or an early Host completion on timeout) does not stop Worker execution.
                // The channel result also covers failures and invocations cancelled before they were sent.
                _ = invocationContext.ResultSource.Task.ContinueWith(
                    result =>
                    {
                        try
                        {
                            _invocationTracker.InvocationCompleted(invocationId, result.IsCompletedSuccessfully);
                        }
                        catch (Exception exception) when (!Utility.IsFatal(exception))
                        {
                            Log.InvocationAccountingFailed(_logger, exception, invocationId, channel.Id);
                        }
                    },
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }

            if (trackActivity)
            {
                channel.OngoingRequests++;
                _ = channel.ObserveRequestCompletionAsync(invocationContext, responseCompleted);
            }

            if (_logger.IsEnabled(LogLevel.Trace))
            {
                Log.PostingInvocation(_logger, invocationContext.ExecutionContext.InvocationId, channel.Id);
            }

            return true;
        }
    }

    /// <summary>
    /// Initializes this dispatcher's local lifecycle; function loading remains owned by the root registry.
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
    /// Queries all initialized channels, including those not yet ready for invocation, and keys their status by worker ID.
    /// </summary>
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

    /// <summary>
    /// Stops local dispatch and allows accepted invocations to drain within the shutdown timeout.
    /// Shared channels remain owned by the root registry and are not disposed here.
    /// </summary>
    public async Task ShutdownAsync()
    {
        PreShutdown();

        // Existing invocations must drain even if replacement setup invalidated the active ready snapshot.
        // The registry owns disposal; shutdown never waits for a future channel.
        List<Task> drainTasks = [];
        foreach (WorkerChannel channel in _channelRegistry.GetInitializedChannels())
        {
            if (IsReadyForInvocations(channel))
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
    public Task<bool> RestartWorkerWithInvocationIdAsync(string invocationId, Exception exception)
        => Task.FromResult(false);

    /// <summary>
    /// Leaves worker creation to the platform; externally linked channels are supplied by the registry.
    /// </summary>
    public Task StartWorkerChannel() => Task.CompletedTask;

    /// <summary>
    /// Rejects new dispatch and cancels pending channel waits before already accepted invocations are drained.
    /// </summary>
    public void PreShutdown()
    {
        if (_disposed || _stopping)
        {
            return;
        }

        // This latch is best effort. A ready-channel invocation racing shutdown may still be accepted and drained.
        _stopping = true;
        State = FunctionInvocationDispatcherState.Disposing;
        _dispatcherStoppedSource.Cancel();
    }

    /// <summary>
    /// Ends this dispatcher's lifecycle and cancels its waits without disposing root-owned channels.
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
        _dispatcherStoppedSource.Cancel();
    }

    private bool TryDispatchInvocation(ScriptInvocationContext invocationContext, IReadOnlyList<WorkerChannel> channels,
        string functionGroup, CancellationToken cancellationToken = default)
    {
        // A selected channel may retire before its lifecycle lock is acquired; try other matching channels.
        for (int attempt = 0; attempt < channels.Count; attempt++)
        {
            if (!TryGetReadyChannel(channels, functionGroup, out WorkerChannel channel))
            {
                return false;
            }

            if (DispatchInvocation(invocationContext, (RpcClientWorkerChannel)channel, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Rechecks a registry snapshot for eligible channels and rotates independently within the requested group.
    /// Traffic for other groups does not advance this group's cursor.
    /// </summary>
    /// <param name="channels">The registry's ready-channel snapshot.</param>
    /// <param name="functionGroup">The group required by the invoked function.</param>
    /// <param name="channel">The selected channel on success; otherwise, null.</param>
    /// <returns>Whether an eligible channel was found.</returns>
    private bool TryGetReadyChannel(IReadOnlyList<WorkerChannel> channels, string functionGroup, [NotNullWhen(true)] out WorkerChannel channel)
    {
        channel = null;
        if (channels.Count == 0)
        {
            return false;
        }

        if (channels.Count == 1)
        {
            // The common one-worker path avoids round-robin synchronization and scanning.
            WorkerChannel candidate = channels[0];
            if (!IsEligible(candidate, functionGroup))
            {
                return false;
            }

            channel = candidate;
            return true;
        }

        List<WorkerChannel> matchingChannels = [];
        for (int i = 0; i < channels.Count; i++)
        {
            WorkerChannel candidate = channels[i];
            if (IsEligible(candidate, functionGroup))
            {
                matchingChannels.Add(candidate);
            }
        }

        if (matchingChannels.Count == 0)
        {
            return false;
        }

        lock (_selectionLock)
        {
            _nextChannelIndices.TryGetValue(functionGroup, out int next);
            next %= matchingChannels.Count;
            _nextChannelIndices[functionGroup] = (next + 1) % matchingChannels.Count;
            channel = matchingChannels[next];
        }

        return true;
    }

    /// <summary>
    /// Waits for an eligible channel in the requested group, respecting cancellation, dispatcher stop, and the wait timeout.
    /// Registry changes trigger resampling so globally ready channels from other groups cannot cause polling.
    /// </summary>
    /// <param name="invocationContext">The invocation awaiting channel admission.</param>
    /// <param name="functionGroup">The group derived from the invocation's validated function metadata.</param>
    /// <param name="readyChannels">The initial ready snapshot or pending readiness task.</param>
    /// <param name="changed">The registry change task captured before obtaining that snapshot, preventing lost wakeups.</param>
    private async Task InvokeWhenChannelIsReadyAsync(
        ScriptInvocationContext invocationContext,
        string functionGroup,
        ValueTask<IReadOnlyList<WorkerChannel>> readyChannels,
        Task changed)
    {
        // Cancellation linking stays off the ready-channel hot path.
        using CancellationTokenSource operationSource =
            CancellationTokenSource.CreateLinkedTokenSource(invocationContext.CancellationToken, _dispatcherStoppedSource.Token);
        operationSource.CancelAfter(_channelWaitTimeout);
        try
        {
            while (true)
            {
                // Completed snapshots bypass WaitAsync, so every retry must explicitly observe cancellation.
                operationSource.Token.ThrowIfCancellationRequested();
                IReadOnlyList<WorkerChannel> channels = readyChannels.IsCompletedSuccessfully
                    ? readyChannels.Result
                    : await readyChannels.AsTask().WaitAsync(operationSource.Token);
                operationSource.Token.ThrowIfCancellationRequested();
                if (TryDispatchInvocation(invocationContext, channels, functionGroup, operationSource.Token))
                {
                    return;
                }

                // Other groups can stay globally ready indefinitely. Resample only after a registry change.
                await changed.WaitAsync(operationSource.Token);
                changed = _channelRegistry.GetChangeTask();
                readyChannels = _channelRegistry.GetReadyChannelsAsync();
            }
        }
        catch (OperationCanceledException) when (!invocationContext.CancellationToken.IsCancellationRequested &&
            !_dispatcherStoppedSource.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"No client-backed worker channel became ready for function '{invocationContext.FunctionMetadata.Name}' in group '{functionGroup}' within {_channelWaitTimeout}.");
        }
    }

    /// <summary>
    /// Checks non-terminal Client readiness for draining, without restricting the function group.
    /// Readiness permits buffering invocations before individual function-load responses arrive.
    /// </summary>
    private static bool IsReadyForInvocations(WorkerChannel channel) => channel is RpcClientWorkerChannel { IsReadyForInvocations: true };

    /// <summary>
    /// Requires a ready serving channel with the same assigned group, using case-insensitive platform identity.
    /// Validation workers are excluded; listener-specific exceptions such as HTTP/blob sharing do not apply to invocation routing.
    /// </summary>
    private static bool IsEligible(WorkerChannel channel, string functionGroup)
        => channel is RpcClientWorkerChannel { IsReadyForInvocations: true } client
            && !string.Equals(client.FunctionGroupName, FunctionGroups.ValidationWorker, StringComparison.OrdinalIgnoreCase)
            && string.Equals(client.FunctionGroupName, functionGroup, StringComparison.OrdinalIgnoreCase);

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Trace, "Posting invocation {InvocationId} on worker {WorkerId}.")]
        public static partial void PostingInvocation(ILogger logger, Guid invocationId, string workerId);

        [LoggerMessage(1, LogLevel.Debug, "{Dispatcher} received no functions.")]
        public static partial void NoFunctions(ILogger logger, string dispatcher);

        [LoggerMessage(2, LogLevel.Debug, "Draining client-backed worker invocations timed out during dispatcher shutdown.")]
        public static partial void DrainTimedOut(ILogger logger);

        [LoggerMessage(3, LogLevel.Error, "Failed to finalize billing accounting for invocation {InvocationId} on worker {WorkerId}.")]
        public static partial void InvocationAccountingFailed(ILogger logger, Exception exception, string invocationId, string workerId);
    }
}
