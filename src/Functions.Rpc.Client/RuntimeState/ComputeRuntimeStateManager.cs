// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Computes timestamped worker counts and HTTP capacity from the worker registry and ScriptHost state.
/// </summary>
/// <remarks>
/// <para>
/// Capacity uses a fixed concurrency of 16 for each initialized HTTP-group worker that is ready for invocations.
/// It is zero unless ScriptHost is running and the Host is not stopping. Active invocations do not reduce this total.
/// Worker counts include initialized, linked workers regardless of invocation readiness or ScriptHost state.
/// </para>
/// <para>
/// A future improvement could propagate concurrency from the worker assignment payload through WorkerProxy to the Host,
/// like <see cref="RpcClientWorkerChannel.FunctionGroupName"/>, allowing the platform to set different concurrency limits per worker.
/// </para>
/// <para>
/// The manager recomputes the snapshot when linked channels change, the ScriptHost state changes, or a linked
/// HTTP worker completes dispatcher setup and becomes ready. It only retries failed recomputes after a delay.
/// Once the application starts stopping, the manager records zero capacity and never records positive capacity again.
/// </para>
/// <para>
/// An unexpected tracking failure withdraws capacity and preserves the last worker counts without stopping the Host.
/// </para>
/// </remarks>
internal sealed partial class ComputeRuntimeStateManager : BackgroundService, IComputeRuntimeStateManager
{
    private const int DefaultHttpWorkerConcurrency = 16;

    /// <summary>
    /// Gets the delay before a failed recompute is retried when no change wakes the manager first.
    /// </summary>
    private static readonly TimeSpan RecomputeRetryDelay = TimeSpan.FromSeconds(1);

    private readonly IWorkerChannelRegistry _channelRegistry;
    private readonly IScriptHostManager _scriptHostManager;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ComputeRuntimeStateManager> _logger;
    private readonly Lock _stateLock = new();
    private TaskCompletionSource _stateChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ComputeRuntimeState _current;
    private bool _stopping;
    private CancellationTokenRegistration _stoppingRegistration;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComputeRuntimeStateManager"/> class.
    /// </summary>
    /// <param name="channelRegistry">The root-owned registry of workers contributing capacity.</param>
    /// <param name="scriptHostManager">The ScriptHost manager whose state gates capacity.</param>
    /// <param name="applicationLifetime">The root Host's application lifetime, used to withdraw capacity on shutdown.</param>
    /// <param name="timeProvider">The UTC clock for snapshots and time source for recompute retries.</param>
    /// <param name="logger">The logger for runtime state diagnostics.</param>
    public ComputeRuntimeStateManager(
        IWorkerChannelRegistry channelRegistry,
        IScriptHostManager scriptHostManager,
        IHostApplicationLifetime applicationLifetime,
        TimeProvider timeProvider,
        ILogger<ComputeRuntimeStateManager> logger)
    {
        _channelRegistry = channelRegistry ?? throw new ArgumentNullException(nameof(channelRegistry));
        _scriptHostManager = scriptHostManager ?? throw new ArgumentNullException(nameof(scriptHostManager));
        _applicationLifetime = applicationLifetime ?? throw new ArgumentNullException(nameof(applicationLifetime));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _current = new(_timeProvider.GetUtcNow(), HttpCapacity: 0, WorkerCount: 0, HttpWorkerCount: 0);
    }

    /// <inheritdoc />
    public ComputeRuntimeState Current
    {
        get
        {
            lock (_stateLock)
            {
                return _current;
            }
        }
    }

    /// <inheritdoc />
    public async Task<ComputeRuntimeState> WaitForChangeAsync(ComputeRuntimeState lastKnownState, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lastKnownState);
        cancellationToken.ThrowIfCancellationRequested();

        while (true)
        {
            Task changed;
            lock (_stateLock)
            {
                if (!ReferenceEquals(_current, lastKnownState))
                {
                    return _current;
                }

                changed = _stateChanged.Task;
            }

            await changed.WaitAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _stoppingRegistration = _applicationLifetime.ApplicationStopping.Register(
            static state => ((ComputeRuntimeStateManager)state!).WithdrawCapacity(stopping: true), this);

        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        WithdrawCapacity(stopping: true);
        return base.StopAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        // The container may dispose this instance once per forwarded service registration; both calls are idempotent.
        _stoppingRegistration.Dispose();
        base.Dispose();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RecomputeUntilStoppedAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            // BackgroundService failures stop the Host by default; a tracking failure must never do that.
            Log.TrackingFailed(_logger, exception);
        }
        finally
        {
            WithdrawCapacity(stopping: false);
        }
    }

    /// <summary>
    /// Recomputes the snapshot until shutdown, waking only when an input to counts or capacity changes.
    /// </summary>
    /// <param name="cancellationToken">The token that stops the loop.</param>
    private async Task RecomputeUntilStoppedAsync(CancellationToken cancellationToken)
    {
        // Keep pending waits across passes: cancelling them after every pass would throw inside their sources.
        Task<long>? initializedChannelsChanged = null;
        Task<long>? scriptHostStateChanged = null;
        List<Task> wakeups = [];
        while (!cancellationToken.IsCancellationRequested)
        {
            // Read the inputs first so a change during the recompute triggers another pass.
            long initializedChannelsVersion = _channelRegistry.InitializedChannelsVersion;
            long scriptHostStateVersion = _scriptHostManager.StateVersion;
            ScriptHostState scriptHostState = _scriptHostManager.State;

            // A pending registry wait targets a version no newer than this one, so it still completes on the next change.
            if (initializedChannelsChanged is null || initializedChannelsChanged.IsCompleted)
            {
                initializedChannelsChanged = _channelRegistry.WaitForInitializedChannelsChangeAsync(initializedChannelsVersion, cancellationToken);
            }

            if (scriptHostStateChanged is null || scriptHostStateChanged.IsCompleted)
            {
                scriptHostStateChanged = _scriptHostManager.WaitForStateChangeAsync(scriptHostStateVersion, cancellationToken);
            }

            wakeups.Clear();
            wakeups.Add(initializedChannelsChanged);
            wakeups.Add(scriptHostStateChanged);
            try
            {
                Recompute(scriptHostState, scriptHostStateVersion, wakeups);
            }
            catch (Exception exception)
            {
                Log.RecomputeFailed(_logger, exception);

                // Retry after a delay in case no input changes first.
                wakeups.Add(Task.Delay(RecomputeRetryDelay, _timeProvider, cancellationToken));
            }

            await Task.WhenAny(wakeups);

            if (initializedChannelsChanged.IsFaulted)
            {
                // The root registry was disposed during Host shutdown.
                if (initializedChannelsChanged.Exception.InnerException is ObjectDisposedException)
                {
                    return;
                }

                // Surface any other failure instead of retrying it in a tight loop.
                await initializedChannelsChanged;
            }

            if (scriptHostStateChanged.IsFaulted)
            {
                // Surface the failure instead of retrying it in a tight loop.
                await scriptHostStateChanged;
            }
        }
    }

    /// <summary>
    /// Counts linked workers and sums the fixed default concurrency of ready HTTP workers.
    /// </summary>
    /// <param name="scriptHostState">The ScriptHost state read before the recompute.</param>
    /// <param name="scriptHostStateVersion">The lifecycle version captured before reading the state.</param>
    /// <param name="wakeups">Receives pending setup and readiness tasks for linked HTTP workers.</param>
    private void Recompute(ScriptHostState scriptHostState, long scriptHostStateVersion, List<Task> wakeups)
    {
        long httpCapacity = 0;
        int httpWorkerCount = 0;
        IReadOnlyList<WorkerChannel> channels = _channelRegistry.GetInitializedChannels();
        IRpcClientFunctionInvocationDispatcher? dispatcher = scriptHostState is ScriptHostState.Running
            ? GetActiveDispatcher()
            : null;
        foreach (WorkerChannel channel in channels)
        {
            if (channel is not RpcClientWorkerChannel { IsHttpFunctionGroup: true })
            {
                continue;
            }

            httpWorkerCount++;
            if (dispatcher is not { State: FunctionInvocationDispatcherState.Initialized })
            {
                continue;
            }

            // Observe the dispatcher's full setup outcome, not just buffers created before load requests are sent.
            Task setup = dispatcher.GetChannelSetupTask(channel);
            bool setupPending = !setup.IsCompleted;
            if (!setup.IsCompletedSuccessfully)
            {
                if (setupPending)
                {
                    wakeups.Add(setup);
                }

                continue;
            }

            // The channel becomes ready before its readiness task completes, so read the task first. A channel that is
            // not ready with a completed task is being disposed, and the registry signals when it is removed.
            Task readiness = channel.InvocationBuffersInitialization;
            bool readinessPending = !readiness.IsCompleted;
            if (channel.IsChannelReadyForInvocations())
            {
                httpCapacity += DefaultHttpWorkerConcurrency;
            }
            else if (readinessPending)
            {
                wakeups.Add(readiness);
            }
        }

        UpdateSnapshot(httpCapacity, scriptHostState, scriptHostStateVersion, channels.Count, httpWorkerCount);
    }

    private IRpcClientFunctionInvocationDispatcher? GetActiveDispatcher()
    {
        try
        {
            return _scriptHostManager.Services?.GetRequiredService<IRpcClientFunctionInvocationDispatcher>();
        }
        catch (ObjectDisposedException)
        {
            Log.ScriptHostDisposed(_logger);
            return null;
        }
    }

    /// <summary>
    /// Withdraws capacity even if the registry, ScriptHost, or configured clock is unavailable.
    /// </summary>
    /// <param name="stopping">Whether shutdown has begun, preventing later recomputes from restoring capacity.</param>
    private void WithdrawCapacity(bool stopping)
    {
        ComputeRuntimeState snapshot;
        TaskCompletionSource changed;
        Exception? clockFailure = null;
        lock (_stateLock)
        {
            _stopping |= stopping;
            stopping = _stopping;
            if (_current.HttpCapacity == 0)
            {
                return;
            }

            DateTimeOffset createdTime;
            try
            {
                createdTime = _timeProvider.GetUtcNow();
            }
            catch (Exception exception)
            {
                createdTime = TimeProvider.System.GetUtcNow();
                clockFailure = exception;
            }

            snapshot = _current with { CreatedTime = createdTime, HttpCapacity = 0 };
            changed = ReplaceSnapshotLocked(snapshot);
        }

        changed.TrySetResult();
        if (clockFailure is not null)
        {
            Log.WithdrawalClockFailed(_logger, clockFailure);
        }

        Log.CapacityWithdrawn(_logger, snapshot.CreatedTime, snapshot.WorkerCount, snapshot.HttpWorkerCount, stopping);
    }

    /// <summary>
    /// Replaces the current snapshot and wakes waiters when counts or capacity changed.
    /// </summary>
    /// <remarks>
    /// Capacity is forced to zero once stopping. UTC creation time is captured only when state changes.
    /// </remarks>
    /// <param name="httpCapacity">The total concurrency of ready HTTP workers.</param>
    /// <param name="scriptHostState">The ScriptHost state observed for capacity, used only for logging.</param>
    /// <param name="scriptHostStateVersion">The lifecycle version used for this calculation.</param>
    /// <param name="workerCount">The initialized worker count.</param>
    /// <param name="httpWorkerCount">The initialized HTTP worker count.</param>
    private void UpdateSnapshot(long httpCapacity, ScriptHostState scriptHostState, long scriptHostStateVersion,
        int workerCount, int httpWorkerCount)
    {
        ComputeRuntimeState snapshot;
        TaskCompletionSource changed;
        bool stopping;
        lock (_stateLock)
        {
            stopping = _stopping;
            if (stopping || scriptHostStateVersion != _scriptHostManager.StateVersion)
            {
                // Never publish capacity computed across a restart or restore it after stopping.
                httpCapacity = 0;
            }

            if (_current.HttpCapacity == httpCapacity && _current.WorkerCount == workerCount && _current.HttpWorkerCount == httpWorkerCount)
            {
                return;
            }

            snapshot = new(_timeProvider.GetUtcNow(), httpCapacity, workerCount, httpWorkerCount);
            changed = ReplaceSnapshotLocked(snapshot);
        }

        // Signal outside the lock so waiter continuations never run under it.
        changed.TrySetResult();
        Log.StateChanged(_logger, snapshot.CreatedTime, snapshot.HttpCapacity, snapshot.WorkerCount, snapshot.HttpWorkerCount,
            scriptHostState, stopping);
    }

    // Requires _stateLock. The caller signals waiters after releasing it.
    private TaskCompletionSource ReplaceSnapshotLocked(ComputeRuntimeState snapshot)
    {
        _current = snapshot;
        TaskCompletionSource changed = _stateChanged;
        _stateChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);

        return changed;
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Information,
            "Compute runtime state created at {CreatedTime}: HTTP capacity {HttpCapacity}. " +
            "Workers: {WorkerCount}. HTTP workers: {HttpWorkerCount}. " +
            "ScriptHost state: {ScriptHostState}. Host stopping: {Stopping}.")]
        public static partial void StateChanged(ILogger logger, DateTimeOffset createdTime, long httpCapacity, int workerCount, int httpWorkerCount,
            ScriptHostState scriptHostState, bool stopping);

        [LoggerMessage(1, LogLevel.Error, "Failed to compute the compute runtime state. The previous snapshot remains current.")]
        public static partial void RecomputeFailed(ILogger logger, Exception exception);

        [LoggerMessage(2, LogLevel.Error,
            "Compute runtime state tracking stopped unexpectedly. Withdrawing HTTP capacity.")]
        public static partial void TrackingFailed(ILogger logger, Exception exception);

        [LoggerMessage(3, LogLevel.Debug, "ScriptHost services were disposed while computing HTTP capacity.")]
        public static partial void ScriptHostDisposed(ILogger logger);

        [LoggerMessage(4, LogLevel.Information,
            "Compute runtime state created at {CreatedTime}: HTTP capacity withdrawn. " +
            "Workers: {WorkerCount}. HTTP workers: {HttpWorkerCount}. Host stopping: {Stopping}.")]
        public static partial void CapacityWithdrawn(ILogger logger, DateTimeOffset createdTime, int workerCount, int httpWorkerCount, bool stopping);

        [LoggerMessage(5, LogLevel.Warning,
            "Failed to read the configured UTC clock while withdrawing HTTP capacity. The system UTC clock was used.")]
        public static partial void WithdrawalClockFailed(ILogger logger, Exception exception);
    }
}
