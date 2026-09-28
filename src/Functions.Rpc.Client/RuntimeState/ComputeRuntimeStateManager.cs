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
/// Registry changes wake the manager immediately. ScriptHost state and channel readiness have no awaitable signal, so the
/// manager also polls them while at least one channel is linked. Once the application starts stopping, the manager
/// records zero counts and never records positive counts again.
/// </para>
/// </remarks>
internal sealed partial class ComputeRuntimeStateManager : IComputeRuntimeStateManager, IHostedService, IDisposable
{
    // Host logging keeps only system-prefixed categories, so the ILogger<T> type-name category would be dropped.
    internal const string LogCategory = "Host.ComputeRuntimeState";

    /// <summary>
    /// Gets the interval at which ScriptHost state and channel readiness are rechecked while a channel is linked.
    /// </summary>
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(250);

    private readonly IWorkerChannelRegistry _channelRegistry;
    private readonly IScriptHostManager _scriptHostManager;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly TimeSpan _pollInterval;
    private readonly Lock _stateLock = new();
    private readonly CancellationTokenSource _shutdownSource = new();
    private TaskCompletionSource _stateChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ComputeRuntimeState _current;
    private bool _stopping;
    private CancellationTokenRegistration _stoppingRegistration;
    private Task? _runTask;
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComputeRuntimeStateManager"/> class.
    /// </summary>
    /// <param name="channelRegistry">The root-owned worker channel registry whose initialized channels are counted.</param>
    /// <param name="scriptHostManager">The ScriptHost manager whose state gates the counts.</param>
    /// <param name="applicationLifetime">The root Host's application lifetime, used to record zero counts on shutdown.</param>
    /// <param name="timeProvider">The time source for snapshot versions and polling.</param>
    /// <param name="loggerFactory">The factory used to create the <see cref="LogCategory"/> logger.</param>
    public ComputeRuntimeStateManager(
        IWorkerChannelRegistry channelRegistry,
        IScriptHostManager scriptHostManager,
        IHostApplicationLifetime applicationLifetime,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
        : this(channelRegistry, scriptHostManager, applicationLifetime, timeProvider, loggerFactory, DefaultPollInterval)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ComputeRuntimeStateManager"/> class with a custom poll interval.
    /// </summary>
    /// <param name="channelRegistry">The root-owned worker channel registry whose initialized channels are counted.</param>
    /// <param name="scriptHostManager">The ScriptHost manager whose state gates the counts.</param>
    /// <param name="applicationLifetime">The root Host's application lifetime, used to record zero counts on shutdown.</param>
    /// <param name="timeProvider">The time source for snapshot versions and polling.</param>
    /// <param name="loggerFactory">The factory used to create the <see cref="LogCategory"/> logger.</param>
    /// <param name="pollInterval">The interval at which state is rechecked while a channel is linked.</param>
    internal ComputeRuntimeStateManager(
        IWorkerChannelRegistry channelRegistry,
        IScriptHostManager scriptHostManager,
        IHostApplicationLifetime applicationLifetime,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        TimeSpan pollInterval)
    {
        _channelRegistry = channelRegistry ?? throw new ArgumentNullException(nameof(channelRegistry));
        _scriptHostManager = scriptHostManager ?? throw new ArgumentNullException(nameof(scriptHostManager));
        _applicationLifetime = applicationLifetime ?? throw new ArgumentNullException(nameof(applicationLifetime));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = (loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory))).CreateLogger(LogCategory);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pollInterval, TimeSpan.Zero);
        _pollInterval = pollInterval;
        _current = new(GetNowUnixMilliseconds(), LinkedWorkerCount: 0, LinkedHttpWorkerCount: 0);
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
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_stateLock)
        {
            if (_runTask is not null)
            {
                return Task.CompletedTask;
            }

            _runTask = RunAsync(_shutdownSource.Token);
        }

        _stoppingRegistration = _applicationLifetime.ApplicationStopping.Register(
            static state => ((ComputeRuntimeStateManager)state!).OnStopping(), this);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        OnStopping();
        await _shutdownSource.CancelAsync();

        Task? runTask;
        lock (_stateLock)
        {
            runTask = _runTask;
        }

        if (runTask is not null)
        {
            await runTask.WaitAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // The container may dispose this instance once per forwarded service registration.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stoppingRegistration.Dispose();
        _shutdownSource.Cancel();
        _shutdownSource.Dispose();
    }

    /// <summary>
    /// Recomputes the snapshot until shutdown, waking on registry changes and polling while a channel is linked.
    /// </summary>
    /// <param name="cancellationToken">The token that stops the loop.</param>
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        // Hosted-service startup must not run the first recompute synchronously.
        await Task.Yield();

        // Keep one registry wait across passes: cancelling it after every poll would throw inside the registry.
        Task<long>? initializedChannelsChanged = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            // Read the version before counting so a change during the recompute triggers another pass.
            long initializedChannelsVersion = _channelRegistry.InitializedChannelsVersion;
            bool shouldPoll;
            try
            {
                shouldPoll = Recompute();
            }
            catch (Exception exception)
            {
                Log.RecomputeFailed(_logger, exception);

                // Keep polling so the next pass retries the recompute.
                shouldPoll = true;
            }

            // A pending wait targets a version no newer than this one, so it still completes on the next change.
            if (initializedChannelsChanged is null || initializedChannelsChanged.IsCompleted)
            {
                initializedChannelsChanged = _channelRegistry.WaitForInitializedChannelsChangeAsync(initializedChannelsVersion, cancellationToken);
            }

            using CancellationTokenSource delaySource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            await Task.WhenAny(
                initializedChannelsChanged,
                Task.Delay(shouldPoll ? _pollInterval : Timeout.InfiniteTimeSpan, _timeProvider, delaySource.Token));
            await delaySource.CancelAsync();

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
        }
    }

    /// <summary>
    /// Counts the linked workers and linked HTTP workers, then updates the snapshot.
    /// </summary>
    /// <returns><see langword="true"/> if any channel is linked, so the caller must keep polling; otherwise, <see langword="false"/>.</returns>
    private bool Recompute()
    {
        IReadOnlyList<WorkerChannel> channels = _channelRegistry.GetInitializedChannels();
        int linkedWorkerCount = 0;
        int linkedHttpWorkerCount = 0;
        ScriptHostState scriptHostState = _scriptHostManager.State;
        if (scriptHostState is ScriptHostState.Running)
        {
            linkedWorkerCount = channels.Count;
            foreach (WorkerChannel channel in channels)
            {
                if (channel is RpcClientWorkerChannel { IsHttpFunctionGroup: true } && channel.IsChannelReadyForInvocations())
                {
                    linkedHttpWorkerCount++;
                }
            }
        }

        UpdateSnapshot(linkedWorkerCount, linkedHttpWorkerCount, scriptHostState);
        return channels.Count > 0;
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
    /// Counts are forced to zero once stopping. Snapshot versions increase strictly and track Unix milliseconds, so they
    /// keep increasing across process restarts.
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

            long snapshotVersion = Math.Max(_current.SnapshotVersion + 1, GetNowUnixMilliseconds());
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
    /// Gets the current time in Unix milliseconds from the configured <see cref="TimeProvider"/>.
    /// </summary>
    private long GetNowUnixMilliseconds() => _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Information,
            "Compute runtime state changed to snapshot version {SnapshotVersion}: {LinkedWorkerCount} linked worker(s), " +
            "{LinkedHttpWorkerCount} linked HTTP worker(s). ScriptHost state: {ScriptHostState}. Host stopping: {Stopping}.")]
        public static partial void StateChanged(ILogger logger, long snapshotVersion, int linkedWorkerCount, int linkedHttpWorkerCount,
            ScriptHostState scriptHostState, bool stopping);

        [LoggerMessage(1, LogLevel.Error, "Failed to compute the compute runtime state. The previous snapshot remains current.")]
        public static partial void RecomputeFailed(ILogger logger, Exception exception);
    }
}
