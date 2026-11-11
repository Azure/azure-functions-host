// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Reconciles worker-gated listeners with the function groups that ready workers can currently serve.
/// </summary>
/// <remarks>
/// One manager belongs to each ScriptHost. Its reconciliation loop starts with the first registered listener and wakes
/// on registry changes, channel terminal signals, registrations, completed listener operations, and due retries. The
/// loop never awaits a listener operation, so a slow start or stop for one function cannot delay any other function.
/// Each listener has at most one operation in flight and its own retry backoff. Disposal ends the loop; listener
/// generations remain owned by their <see cref="WorkerGatedListener"/>.
/// </remarks>
internal sealed partial class WorkerGatedListenerManager(
    IWorkerChannelRegistry channelRegistry,
    TimeProvider timeProvider,
    ILogger<WorkerGatedListenerManager> logger) : IDisposable
{
    internal static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(1);

    private readonly IWorkerChannelRegistry _channelRegistry = channelRegistry ?? throw new ArgumentNullException(nameof(channelRegistry));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ILogger<WorkerGatedListenerManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly CancellationTokenSource _shutdownSource = new();
    // Event Grid Blob functions share an SDK listener, so their start-only transitions must be serialized.
    private readonly SemaphoreSlim _startOnlyStartLock = new(1, 1);

    // Guards the registration list, its change signal, and lazy loop creation.
    private readonly Lock _lock = new();
    private readonly List<WorkerGatedListener> _listeners = [];
    private TaskCompletionSource _registrationChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task _reconcileTask;
    private bool _disposed;

    internal Task ReconcileTask
    {
        get
        {
            lock (_lock)
            {
                return _reconcileTask ?? Task.CompletedTask;
            }
        }
    }

    internal void Register(WorkerGatedListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_listeners.Contains(listener))
            {
                _listeners.Add(listener);
            }

            _registrationChanged.TrySetResult();
            _reconcileTask ??= Task.Run(() => ReconcileAsync(_shutdownSource.Token));
        }
    }

    internal void Unregister(WorkerGatedListener listener)
    {
        lock (_lock)
        {
            _listeners.Remove(listener);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _listeners.Clear();
        }

        // The loop observes cancellation and exits on its own; listener owners stop and dispose their generations.
        _shutdownSource.Cancel();
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        // Loop-owned state; only this loop reads or writes it.
        Dictionary<WorkerGatedListener, ListenerState> states = [];
        TimeSpan loopRetryDelay = InitialRetryDelay;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!await ReconcileOnceAsync(states, cancellationToken))
                {
                    return;
                }

                loopRetryDelay = InitialRetryDelay;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Never let the loop die silently; gating resumes after a bounded backoff.
                Log.ReconcileLoopFailed(_logger, exception, loopRetryDelay);
                if (!await TryDelayAsync(loopRetryDelay, cancellationToken))
                {
                    return;
                }

                loopRetryDelay = NextRetryDelay(loopRetryDelay);
            }
        }
    }

    // Starts any needed listener operations, then waits for the next wake signal.
    // Returns false when the channel registry has been disposed.
    private async Task<bool> ReconcileOnceAsync(Dictionary<WorkerGatedListener, ListenerState> states, CancellationToken cancellationToken)
    {
        WorkerGatedListener[] listeners;
        Task registrationChanged;
        lock (_lock)
        {
            if (_registrationChanged.Task.IsCompleted)
            {
                _registrationChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            registrationChanged = _registrationChanged.Task;
            listeners = [.. _listeners];
        }

        // Obtain the change task before sampling registry inputs so no membership or readiness change is missed.
        Task registryChanged;
        IReadOnlyList<WorkerChannel> initializedChannels;
        try
        {
            registryChanged = _channelRegistry.GetChangeTask();
            initializedChannels = _channelRegistry.GetInitializedChannels();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        List<Task> wakeTasks = [registryChanged, registrationChanged];
        foreach (WorkerChannel channel in initializedChannels)
        {
            if (channel is RpcClientWorkerChannel client && !client.IsTerminated)
            {
                wakeTasks.Add(client.Completion);
                wakeTasks.Add(client.FunctionLoadFailure);
            }
        }

        ValueTask<IReadOnlyList<WorkerChannel>> readiness = _channelRegistry.GetReadyChannelsAsync();
        IReadOnlyList<WorkerChannel> channels = readiness.IsCompletedSuccessfully ? readiness.Result : [];
        Dictionary<string, bool> availability = listeners.Select(listener => listener.FunctionGroup).Distinct(StringComparer.Ordinal).ToDictionary(
            group => group, group => channels.Any(channel => channel is RpcClientWorkerChannel client && client.CanServeFunctionGroup(group)));
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateTimeOffset? nextRetry = null;
        HashSet<WorkerGatedListener> registered = [.. listeners];
        foreach (WorkerGatedListener listener in states.Keys.Where(listener => !registered.Contains(listener)).ToArray())
        {
            states.Remove(listener);
        }

        foreach (WorkerGatedListener listener in listeners)
        {
            if (!states.TryGetValue(listener, out ListenerState state))
            {
                state = new();
                states.Add(listener, state);
            }

            bool desired = availability[listener.FunctionGroup];
            // All Blob functions share one gate. Apply the sampled group state here, not from stale listener operations.
            listener.BlobQueueGate?.SetEnabled(desired);
            if (state.Operation is { IsCompleted: false } running)
            {
                if (state.Requested && !desired)
                {
                    // Signal worker loss to a start still in progress.
                    try
                    {
                        state.OperationCancellation?.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        // The operation completed and disposed its cancellation source after the state check.
                    }
                    catch (AggregateException exception)
                    {
                        Log.OperationCancellationFailed(_logger, exception, listener.FunctionName);
                    }
                }

                wakeTasks.Add(running);
                continue;
            }

            if (state.Operation is { } completed)
            {
                bool operationCanceled = state.OperationCancellation?.IsCancellationRequested == true;
                state.Operation = null;
                state.OperationCancellation = null;
                if (await completed)
                {
                    state.Applied = state.Requested;
                    state.RetryDelay = InitialRetryDelay;
                    state.RetryAt = null;
                }
                else
                {
                    state.Applied = null;
                    if (operationCanceled)
                    {
                        state.RetryAt = null;
                        state.RetryDelay = InitialRetryDelay;
                    }
                    else
                    {
                        state.RetryAt = now + state.RetryDelay;
                        Log.RetryingListener(_logger, listener.FunctionName, state.RetryDelay);
                        state.RetryDelay = NextRetryDelay(state.RetryDelay);
                    }
                }
            }

            if (state.RetryAt is { } retryAt && desired == state.Requested)
            {
                if (retryAt > now)
                {
                    nextRetry = nextRetry is null || retryAt < nextRetry ? retryAt : nextRetry;
                    continue;
                }
            }
            else if (state.Applied == desired)
            {
                continue;
            }

            state.Requested = desired;
            state.RetryAt = null;
            CancellationTokenSource operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            state.OperationCancellation = operationCancellation;
            state.Operation = ReconcileListenerAsync(listener, desired, operationCancellation);
            wakeTasks.Add(state.Operation);
        }

        using CancellationTokenSource retrySource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (nextRetry is { } due)
        {
            wakeTasks.Add(Task.Delay(due - now, _timeProvider, retrySource.Token));
        }

        try
        {
            await Task.WhenAny(wakeTasks).WaitAsync(cancellationToken);
        }
        finally
        {
            retrySource.Cancel();
        }

        foreach (Task wakeTask in wakeTasks)
        {
            if (wakeTask.IsFaulted)
            {
                _ = wakeTask.Exception;
            }
        }

        return true;
    }

    private async Task<bool> TryDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, _timeProvider, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static TimeSpan NextRetryDelay(TimeSpan delay) => TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRetryDelay.Ticks));

    private async Task<bool> ReconcileListenerAsync(
        WorkerGatedListener listener, bool start, CancellationTokenSource operationCancellation)
    {
        CancellationToken cancellationToken = operationCancellation.Token;
        try
        {
            // Run listener work off the reconciliation loop, even when the listener completes synchronously.
            await Task.Yield();
            if (start)
            {
                if (listener.IsStartOnly)
                {
                    using SemaphoreLock startLock = await _startOnlyStartLock.LockAsync(cancellationToken);
                    return await listener.StartListeningAsync(cancellationToken);
                }

                return await listener.StartListeningAsync(cancellationToken);
            }

            return await listener.StopListeningAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return !start;
        }
        catch (ObjectDisposedException)
        {
            // The listener was disposed while it was being reconciled.
            return true;
        }
        catch (Exception exception)
        {
            Log.ReconcileFailed(_logger, exception, listener.FunctionName);
            return false;
        }
        finally
        {
            operationCancellation.Dispose();
        }
    }

    private sealed class ListenerState
    {
        public Task<bool> Operation { get; set; }

        public CancellationTokenSource OperationCancellation { get; set; }

        // The worker availability most recently requested and the one last applied successfully.
        public bool Requested { get; set; }

        public bool? Applied { get; set; }

        public DateTimeOffset? RetryAt { get; set; }

        public TimeSpan RetryDelay { get; set; } = InitialRetryDelay;
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Debug, "Retrying the worker-gated listener for function '{FunctionName}' in {RetryDelay}.")]
        public static partial void RetryingListener(ILogger logger, string functionName, TimeSpan retryDelay);

        [LoggerMessage(1, LogLevel.Error, "Failed to reconcile the worker-gated listener for function '{FunctionName}'.")]
        public static partial void ReconcileFailed(ILogger logger, Exception exception, string functionName);

        [LoggerMessage(2, LogLevel.Error, "The worker-gated listener reconciliation loop failed. Retrying in {RetryDelay}.")]
        public static partial void ReconcileLoopFailed(ILogger logger, Exception exception, TimeSpan retryDelay);

        [LoggerMessage(3, LogLevel.Warning, "A cancellation callback failed while canceling the listener for function '{FunctionName}'.")]
        public static partial void OperationCancellationFailed(ILogger logger, Exception exception, string functionName);
    }
}
