// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Computes versioned linked-worker snapshots from the worker registry and ScriptHost state.
/// </summary>
/// <remarks>
/// <para>
/// Linked workers are the registry's initialized channels. Linked HTTP workers are the subset that advertised the HTTP
/// function group and are ready for invocations. Both counts are zero unless ScriptHost is running and the Host is not
/// stopping.
/// </para>
/// <para>
/// The manager recomputes both counts whenever the set of linked channels changes, the ScriptHost state changes, or a linked
/// HTTP worker becomes ready. It does no other work, except to retry a failed recompute after a delay. Once the application
/// starts stopping, the manager records zero counts and never records positive counts again.
/// </para>
/// <para>
/// An unexpected tracking failure is logged and never stops the Host; the counts then stay unchanged until the Host stops.
/// </para>
/// </remarks>
internal sealed partial class ComputeRuntimeStateManager : BackgroundService, IComputeRuntimeStateManager
{
    // Host logging keeps only system-prefixed categories, so the ILogger<T> type-name category would be dropped.
    internal const string LogCategory = "Host.ComputeRuntimeState";

    /// <summary>
    /// Gets the delay before a failed recompute is retried when no change wakes the manager first.
    /// </summary>
    private static readonly TimeSpan DefaultRecomputeRetryDelay = TimeSpan.FromSeconds(1);

    private readonly IWorkerChannelRegistry _channelRegistry;
    private readonly IScriptHostManager _scriptHostManager;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly TimeSpan _recomputeRetryDelay;
    private readonly Lock _stateLock = new();
    private TaskCompletionSource _stateChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ComputeRuntimeState _current;
    private bool _stopping;
    private CancellationTokenRegistration _stoppingRegistration;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComputeRuntimeStateManager"/> class.
    /// </summary>
    /// <param name="channelRegistry">The root-owned worker channel registry whose initialized channels are counted.</param>
    /// <param name="scriptHostManager">The ScriptHost manager whose state gates the counts.</param>
    /// <param name="applicationLifetime">The root Host's application lifetime, used to record zero counts on shutdown.</param>
    /// <param name="timeProvider">The time source for snapshot versions and recompute retries.</param>
    /// <param name="loggerFactory">The factory used to create the <see cref="LogCategory"/> logger.</param>
    public ComputeRuntimeStateManager(
        IWorkerChannelRegistry channelRegistry,
        IScriptHostManager scriptHostManager,
        IHostApplicationLifetime applicationLifetime,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
        : this(channelRegistry, scriptHostManager, applicationLifetime, timeProvider, loggerFactory, DefaultRecomputeRetryDelay)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ComputeRuntimeStateManager"/> class with a custom recompute retry delay.
    /// </summary>
    /// <param name="channelRegistry">The root-owned worker channel registry whose initialized channels are counted.</param>
    /// <param name="scriptHostManager">The ScriptHost manager whose state gates the counts.</param>
    /// <param name="applicationLifetime">The root Host's application lifetime, used to record zero counts on shutdown.</param>
    /// <param name="timeProvider">The time source for snapshot versions and recompute retries.</param>
    /// <param name="loggerFactory">The factory used to create the <see cref="LogCategory"/> logger.</param>
    /// <param name="recomputeRetryDelay">The delay before a failed recompute is retried when no change wakes the manager first.</param>
    internal ComputeRuntimeStateManager(
        IWorkerChannelRegistry channelRegistry,
        IScriptHostManager scriptHostManager,
        IHostApplicationLifetime applicationLifetime,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        TimeSpan recomputeRetryDelay)
    {
        _channelRegistry = channelRegistry ?? throw new ArgumentNullException(nameof(channelRegistry));
        _scriptHostManager = scriptHostManager ?? throw new ArgumentNullException(nameof(scriptHostManager));
        _applicationLifetime = applicationLifetime ?? throw new ArgumentNullException(nameof(applicationLifetime));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = (loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory))).CreateLogger(LogCategory);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(recomputeRetryDelay, TimeSpan.Zero);
        _recomputeRetryDelay = recomputeRetryDelay;
        _current = new(GetMonotonicMilliseconds(), LinkedWorkerCount: 0, LinkedHttpWorkerCount: 0);
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
    public async Task<ComputeRuntimeState> WaitForChangeAsync(long lastKnownVersion, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        while (true)
        {
            Task changed;
            lock (_stateLock)
            {
                if (_current.SnapshotVersion > lastKnownVersion)
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
            static state => ((ComputeRuntimeStateManager)state!).OnStopping(), this);

        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        OnStopping();
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
    }

    /// <summary>
    /// Recomputes the snapshot until shutdown, waking only when an input to the counts changes.
    /// </summary>
    /// <param name="cancellationToken">The token that stops the loop.</param>
    private async Task RecomputeUntilStoppedAsync(CancellationToken cancellationToken)
    {
        // Keep pending waits across passes: cancelling them after every pass would throw inside their sources.
        Task<long>? initializedChannelsChanged = null;
        Task<ScriptHostState>? scriptHostStateChanged = null;
        ScriptHostState scriptHostStateChangedFrom = default;
        List<Task> wakeups = [];
        while (!cancellationToken.IsCancellationRequested)
        {
            // Read the inputs before counting so a change during the recompute triggers another pass.
            long initializedChannelsVersion = _channelRegistry.InitializedChannelsVersion;
            ScriptHostState scriptHostState = _scriptHostManager.State;

            // A pending registry wait targets a version no newer than this one, so it still completes on the next change.
            if (initializedChannelsChanged is null || initializedChannelsChanged.IsCompleted)
            {
                initializedChannelsChanged = _channelRegistry.WaitForInitializedChannelsChangeAsync(initializedChannelsVersion, cancellationToken);
            }

            // ScriptHost state can return to an earlier value, so reuse a pending wait only if it waits on the state just read.
            if (scriptHostStateChanged is null || scriptHostStateChanged.IsCompleted || scriptHostStateChangedFrom != scriptHostState)
            {
                scriptHostStateChanged = _scriptHostManager.WaitForStateChangeAsync(scriptHostState, cancellationToken);
                scriptHostStateChangedFrom = scriptHostState;
            }

            wakeups.Clear();
            wakeups.Add(initializedChannelsChanged);
            wakeups.Add(scriptHostStateChanged);
            try
            {
                Recompute(scriptHostState, wakeups);
            }
            catch (Exception exception)
            {
                Log.RecomputeFailed(_logger, exception);

                // Retry after a delay in case no input changes first.
                wakeups.Add(Task.Delay(_recomputeRetryDelay, _timeProvider, cancellationToken));
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
    /// Counts the linked workers and linked HTTP workers, then updates the snapshot.
    /// </summary>
    /// <param name="scriptHostState">The ScriptHost state read before the recompute.</param>
    /// <param name="wakeups">Receives the readiness task of each linked HTTP worker that is not yet ready for invocations.</param>
    private void Recompute(ScriptHostState scriptHostState, List<Task> wakeups)
    {
        int linkedWorkerCount = 0;
        int linkedHttpWorkerCount = 0;
        if (scriptHostState is ScriptHostState.Running)
        {
            IReadOnlyList<WorkerChannel> channels = _channelRegistry.GetInitializedChannels();
            linkedWorkerCount = channels.Count;
            foreach (WorkerChannel channel in channels)
            {
                if (channel is not RpcClientWorkerChannel { IsHttpFunctionGroup: true })
                {
                    continue;
                }

                // The channel becomes ready before its readiness task completes, so read the task first. A channel that is
                // not ready with a completed task is being disposed, and the registry signals when it is removed.
                Task readiness = channel.InvocationBuffersInitialization;
                bool readinessPending = !readiness.IsCompleted;
                if (channel.IsChannelReadyForInvocations())
                {
                    linkedHttpWorkerCount++;
                }
                else if (readinessPending)
                {
                    wakeups.Add(readiness);
                }
            }
        }

        UpdateSnapshot(linkedWorkerCount, linkedHttpWorkerCount, scriptHostState);
    }

    /// <summary>
    /// Latches the stopping state and records zero counts. Later calls do nothing.
    /// </summary>
    private void OnStopping()
    {
        lock (_stateLock)
        {
            if (_stopping)
            {
                return;
            }

            _stopping = true;
        }

        UpdateSnapshot(linkedWorkerCount: 0, linkedHttpWorkerCount: 0, _scriptHostManager.State);
    }

    /// <summary>
    /// Replaces the current snapshot and wakes waiters when the counts changed.
    /// </summary>
    /// <remarks>
    /// Counts are forced to zero once stopping. Snapshot versions increase strictly and track the machine's monotonic
    /// clock in milliseconds, so they keep increasing across Host process restarts on the same machine, and wall-clock
    /// changes do not affect them.
    /// </remarks>
    /// <param name="linkedWorkerCount">The number of linked workers.</param>
    /// <param name="linkedHttpWorkerCount">The number of linked HTTP workers that are ready for invocations.</param>
    /// <param name="scriptHostState">The ScriptHost state observed for the counts, used only for logging.</param>
    private void UpdateSnapshot(int linkedWorkerCount, int linkedHttpWorkerCount, ScriptHostState scriptHostState)
    {
        ComputeRuntimeState snapshot;
        TaskCompletionSource changed;
        bool stopping;
        lock (_stateLock)
        {
            stopping = _stopping;
            if (stopping)
            {
                // A recompute that raced the stopping latch must not record positive counts.
                linkedWorkerCount = 0;
                linkedHttpWorkerCount = 0;
            }

            if (_current.LinkedWorkerCount == linkedWorkerCount && _current.LinkedHttpWorkerCount == linkedHttpWorkerCount)
            {
                return;
            }

            long snapshotVersion = Math.Max(_current.SnapshotVersion + 1, GetMonotonicMilliseconds());
            _current = snapshot = new(snapshotVersion, linkedWorkerCount, linkedHttpWorkerCount);
            changed = _stateChanged;
            _stateChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        // Signal outside the lock so waiter continuations never run under it.
        changed.TrySetResult();
        Log.StateChanged(_logger, snapshot.SnapshotVersion, snapshot.LinkedWorkerCount, snapshot.LinkedHttpWorkerCount,
            scriptHostState, stopping);
    }

    /// <summary>
    /// Gets the machine's monotonic clock in milliseconds from the configured <see cref="TimeProvider"/>.
    /// </summary>
    /// <remarks>
    /// The system timestamp keeps counting across process restarts and never moves backward, unlike the wall clock.
    /// <see cref="TimeProvider.GetElapsedTime(long, long)"/> converts it without the overflow that multiplying a
    /// nanosecond timestamp by 1000 would cause.
    /// </remarks>
    private long GetMonotonicMilliseconds()
        => (long)_timeProvider.GetElapsedTime(startingTimestamp: 0, _timeProvider.GetTimestamp()).TotalMilliseconds;

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Information,
            "Compute runtime state changed to snapshot version {SnapshotVersion}: {LinkedWorkerCount} linked worker(s), " +
            "{LinkedHttpWorkerCount} linked HTTP worker(s). ScriptHost state: {ScriptHostState}. Host stopping: {Stopping}.")]
        public static partial void StateChanged(ILogger logger, long snapshotVersion, int linkedWorkerCount, int linkedHttpWorkerCount,
            ScriptHostState scriptHostState, bool stopping);

        [LoggerMessage(1, LogLevel.Error, "Failed to compute the compute runtime state. The previous snapshot remains current.")]
        public static partial void RecomputeFailed(ILogger logger, Exception exception);

        [LoggerMessage(2, LogLevel.Error,
            "Compute runtime state tracking stopped unexpectedly. The counts will not change until the Host stops.")]
        public static partial void TrackingFailed(ILogger logger, Exception exception);
    }
}
