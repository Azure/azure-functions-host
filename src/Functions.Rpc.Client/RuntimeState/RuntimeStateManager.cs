// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Computes linked worker counts and HTTP capacity from the root-owned registry.
/// </summary>
internal sealed partial class RuntimeStateManager : BackgroundService, IRuntimeStateManager
{
    private static readonly TimeSpan RecomputeRetryDelay = TimeSpan.FromSeconds(1);
    private readonly IWorkerChannelRegistry _channelRegistry;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RuntimeStateManager> _logger;
    private readonly Lock _stateLock = new();
    private TaskCompletionSource _stateChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private RuntimeState _current;
    private bool _stopping;
    private CancellationTokenRegistration _stoppingRegistration;

    /// <summary>
    /// Initializes a new instance of the <see cref="RuntimeStateManager"/> class.
    /// Initializes registry-backed worker counts and HTTP capacity tracking.
    /// </summary>
    /// <param name="channelRegistry">The borrowed root-owned registry whose workers contribute counts and capacity.</param>
    /// <param name="applicationLifetime">The root Host lifetime used to permanently withdraw capacity during shutdown.</param>
    /// <param name="timeProvider">The clock for UTC snapshot timestamps and recomputation retries.</param>
    /// <param name="logger">The logger for state changes and tracking failures.</param>
    public RuntimeStateManager(
        IWorkerChannelRegistry channelRegistry,
        IHostApplicationLifetime applicationLifetime,
        TimeProvider timeProvider,
        ILogger<RuntimeStateManager> logger)
    {
        _channelRegistry = channelRegistry ?? throw new ArgumentNullException(nameof(channelRegistry));
        _applicationLifetime = applicationLifetime ?? throw new ArgumentNullException(nameof(applicationLifetime));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _current = new(_timeProvider.GetUtcNow().ToUniversalTime(), HttpCapacity: 0, WorkerCount: 0, HttpWorkerCount: 0);
    }

    /// <inheritdoc />
    public RuntimeState Current
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
    public Task GetChangeTask()
    {
        lock (_stateLock)
        {
            return _stateChanged.Task;
        }
    }

    /// <summary>
    /// Starts registry-backed state tracking and observes root Host shutdown.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels startup.</param>
    /// <returns>A task representing startup of the background tracker.</returns>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _stoppingRegistration = _applicationLifetime.ApplicationStopping.Register(
            static state => ((RuntimeStateManager)state!).WithdrawCapacity(stopping: true), this);
        return base.StartAsync(cancellationToken);
    }

    /// <summary>
    /// Permanently withdraws HTTP capacity and stops the background tracker.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels waiting for the tracker to stop.</param>
    /// <returns>A task representing shutdown of the background tracker.</returns>
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        WithdrawCapacity(stopping: true);
        return base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// Withdraws capacity and releases the shutdown registration and background tracker without disposing the borrowed registry.
    /// </summary>
    public override void Dispose()
    {
        WithdrawCapacity(stopping: true);
        _stoppingRegistration.Dispose();
        base.Dispose();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await TrackChangesAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException)
        {
            Log.RegistryDisposed(_logger);
        }
        catch (Exception exception)
        {
            Log.TrackingFailed(_logger, exception);
        }
        finally
        {
            WithdrawCapacity(stopping: false);
        }
    }

    private async Task TrackChangesAsync(CancellationToken cancellationToken)
    {
        List<Task> wakeups = [];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Capture the invalidation marker before reading registry inputs so concurrent changes trigger another pass.
            Task registryChanged = _channelRegistry.GetChangeTask();

            wakeups.Clear();
            wakeups.Add(registryChanged);
            bool invalidated = false;
            try
            {
                IReadOnlyList<WorkerChannel> initializedChannels = _channelRegistry.GetInitializedChannels();
                int httpWorkerCount = 0;
                foreach (WorkerChannel channel in initializedChannels)
                {
                    if (channel is not RpcClientWorkerChannel clientChannel)
                    {
                        continue;
                    }

                    if (clientChannel.IsHttpFunctionGroup)
                    {
                        httpWorkerCount++;
                    }

                    if (!clientChannel.IsTerminated)
                    {
                        wakeups.Add(clientChannel.Completion);
                        wakeups.Add(clientChannel.FunctionLoadFailure);
                    }
                }

                // This API is a current snapshot, not a membership-change task. Never wait on an already-ready snapshot.
                ValueTask<IReadOnlyList<WorkerChannel>> readiness = _channelRegistry.GetReadyChannelsAsync();
                long httpCapacity = 0;
                if (readiness.IsCompleted)
                {
                    IReadOnlyList<WorkerChannel> readyChannels = readiness.GetAwaiter().GetResult();
                    foreach (WorkerChannel channel in readyChannels)
                    {
                        if (channel is RpcClientWorkerChannel { IsHttpFunctionGroup: true, IsReadyForInvocations: true, MaxHttpConcurrency: { } concurrency })
                        {
                            httpCapacity += concurrency;
                        }
                    }
                }

                RuntimeState? snapshot = null;
                lock (_stateLock)
                {
                    foreach (Task wakeup in wakeups)
                    {
                        invalidated |= wakeup.IsCompleted;
                    }

                    if (!invalidated)
                    {
                        snapshot = ReplaceSnapshotLocked(_stopping ? 0 : httpCapacity, initializedChannels.Count, httpWorkerCount);
                    }
                }

                LogSnapshot(snapshot);
            }
            catch (ObjectDisposedException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Log.RecomputeFailed(_logger, exception);
                WithdrawCapacity(stopping: false);
                wakeups.Add(Task.Delay(RecomputeRetryDelay, _timeProvider, cancellationToken));
            }

            if (!invalidated)
            {
                await Task.WhenAny(wakeups).WaitAsync(cancellationToken);
            }

            // Source faults are not ordinary invalidations. Channel failures are owned/logged by the registry.
            if (registryChanged.IsCompleted)
            {
                await registryChanged;
            }

            foreach (Task wakeup in wakeups)
            {
                if (wakeup.IsFaulted)
                {
                    _ = wakeup.Exception;
                }
            }
        }
    }

    private void WithdrawCapacity(bool stopping)
    {
        RuntimeState? snapshot;
        lock (_stateLock)
        {
            _stopping |= stopping;
            snapshot = ReplaceSnapshotLocked(0, _current.WorkerCount, _current.HttpWorkerCount, withdrawing: true);
        }

        LogSnapshot(snapshot);
    }

    // Callers hold _stateLock. Only changed values create a new timestamp and outward snapshot.
    private RuntimeState? ReplaceSnapshotLocked(long httpCapacity, int workerCount, int httpWorkerCount, bool withdrawing = false)
    {
        if (_current.HttpCapacity == httpCapacity && _current.WorkerCount == workerCount && _current.HttpWorkerCount == httpWorkerCount)
        {
            return null;
        }

        DateTimeOffset createdTime;
        try
        {
            createdTime = _timeProvider.GetUtcNow().ToUniversalTime();
        }
        catch (Exception exception) when (withdrawing)
        {
            // Withdrawal must remain possible even when the configured clock fails.
            Log.WithdrawalClockFailed(_logger, exception);
            createdTime = TimeProvider.System.GetUtcNow();
        }

        _current = new(createdTime, httpCapacity, workerCount, httpWorkerCount);
        TaskCompletionSource changed = _stateChanged;
        _stateChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
        changed.TrySetResult();
        return _current;
    }

    private void LogSnapshot(RuntimeState? snapshot)
    {
        if (snapshot is not null)
        {
            Log.StateChanged(_logger, snapshot.CreatedTime, snapshot.HttpCapacity, snapshot.WorkerCount, snapshot.HttpWorkerCount);
        }
    }

    private static partial class Log
    {
        /// <summary>
        /// Records the counts, capacity, and creation time of a changed snapshot.
        /// </summary>
        [LoggerMessage(900, LogLevel.Information,
            "Compute runtime state created at {CreatedTime}: HTTP capacity {HttpCapacity}. Workers: {WorkerCount}. HTTP workers: {HttpWorkerCount}.")]
        public static partial void StateChanged(ILogger logger, DateTimeOffset createdTime, long httpCapacity, int workerCount, int httpWorkerCount);

        /// <summary>
        /// Records a failed recomputation that temporarily withdraws capacity.
        /// </summary>
        [LoggerMessage(901, LogLevel.Error, "Failed to compute runtime state. HTTP capacity is withdrawn until recomputation succeeds.")]
        public static partial void RecomputeFailed(ILogger logger, Exception exception);

        /// <summary>
        /// Records an unexpected tracking failure that ends capacity calculation.
        /// </summary>
        [LoggerMessage(902, LogLevel.Error, "Compute runtime state tracking stopped unexpectedly. Withdrawing HTTP capacity.")]
        public static partial void TrackingFailed(ILogger logger, Exception exception);

        /// <summary>
        /// Records the end of tracking after registry disposal.
        /// </summary>
        [LoggerMessage(903, LogLevel.Debug, "The worker registry was disposed. Compute runtime state tracking is stopping.")]
        public static partial void RegistryDisposed(ILogger logger);

        /// <summary>
        /// Records use of the system clock when the configured clock fails during capacity withdrawal.
        /// </summary>
        [LoggerMessage(904, LogLevel.Warning, "Failed to read the UTC clock while withdrawing HTTP capacity. Using the system UTC clock.")]
        public static partial void WithdrawalClockFailed(ILogger logger, Exception exception);
    }
}
