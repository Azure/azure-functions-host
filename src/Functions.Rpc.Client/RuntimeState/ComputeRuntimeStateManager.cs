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
/// publishes zero counts and never publishes positive counts again.
/// </para>
/// </remarks>
internal sealed partial class ComputeRuntimeStateManager : IComputeRuntimeStateManager, IHostedService, IDisposable
{
    // Host logging keeps only system-prefixed categories, so the ILogger<T> type-name category would be dropped.
    internal const string LogCategory = "Host.ComputeRuntimeState";

    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(250);

    private readonly IWorkerChannelRegistry _channelRegistry;
    private readonly IScriptHostManager _scriptHostManager;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly TimeSpan _pollInterval;
    private readonly Lock _stateLock = new();
    private readonly CancellationTokenSource _stopSource = new();
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ComputeRuntimeState _current;
    private bool _stopping;
    private CancellationTokenRegistration _stoppingRegistration;
    private Task? _runTask;
    private int _disposed;

    public ComputeRuntimeStateManager(
        IWorkerChannelRegistry channelRegistry,
        IScriptHostManager scriptHostManager,
        IHostApplicationLifetime applicationLifetime,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
        : this(channelRegistry, scriptHostManager, applicationLifetime, timeProvider, loggerFactory, DefaultPollInterval)
    {
    }

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
        _logger = loggerFactory?.CreateLogger(LogCategory) ?? throw new ArgumentNullException(nameof(loggerFactory));
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

                changed = _changed.Task;
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

            _runTask = RunAsync(_stopSource.Token);
        }

        _stoppingRegistration = _applicationLifetime.ApplicationStopping.Register(
            static state => ((ComputeRuntimeStateManager)state!).OnStopping(), this);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        OnStopping();
        await _stopSource.CancelAsync();

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
        _stopSource.Cancel();
        _stopSource.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        // Keep hosted-service startup synchronous work minimal.
        await Task.Yield();

        while (!cancellationToken.IsCancellationRequested)
        {
            // Read the version before counting so a change during the recompute triggers another pass.
            long channelSetVersion = _channelRegistry.ChannelSetVersion;
            bool hasLinkedChannels;
            try
            {
                hasLinkedChannels = Recompute();
            }
            catch (Exception exception)
            {
                Log.RecomputeFailed(_logger, exception);
                hasLinkedChannels = true;
            }

            using CancellationTokenSource waitSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task<long> channelSetChanged = _channelRegistry.WaitForChannelSetChangeAsync(channelSetVersion, waitSource.Token);
            try
            {
                if (hasLinkedChannels)
                {
                    await Task.WhenAny(channelSetChanged, Task.Delay(_pollInterval, _timeProvider, waitSource.Token));
                }
                else
                {
                    await channelSetChanged;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                // The root registry was disposed during Host shutdown.
                return;
            }
            finally
            {
                await waitSource.CancelAsync();
            }

            if (channelSetChanged.IsFaulted && channelSetChanged.Exception.InnerException is ObjectDisposedException)
            {
                return;
            }
        }
    }

    private bool Recompute()
    {
        IReadOnlyList<WorkerChannel> channels = _channelRegistry.GetInitializedChannels();
        int linkedWorkerCount = 0;
        int linkedHttpWorkerCount = 0;
        ScriptHostState scriptHostState = _scriptHostManager.State;
        if (scriptHostState is ScriptHostState.Running)
        {
            foreach (WorkerChannel channel in channels)
            {
                linkedWorkerCount++;
                if (channel is RpcClientWorkerChannel { IsHttpFunctionGroup: true } && channel.IsChannelReadyForInvocations())
                {
                    linkedHttpWorkerCount++;
                }
            }
        }

        Publish(linkedWorkerCount, linkedHttpWorkerCount, scriptHostState);
        return channels.Count > 0;
    }

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

        Publish(linkedWorkerCount: 0, linkedHttpWorkerCount: 0, _scriptHostManager.State);
    }

    private void Publish(int linkedWorkerCount, int linkedHttpWorkerCount, ScriptHostState scriptHostState)
    {
        ComputeRuntimeState published;
        TaskCompletionSource changed;
        bool stopping;
        lock (_stateLock)
        {
            stopping = _stopping;
            if (stopping)
            {
                // A recompute that raced the stopping latch must not publish positive counts.
                linkedWorkerCount = 0;
                linkedHttpWorkerCount = 0;
            }

            if (_current.LinkedWorkerCount == linkedWorkerCount && _current.LinkedHttpWorkerCount == linkedHttpWorkerCount)
            {
                return;
            }

            long snapshotVersion = Math.Max(_current.SnapshotVersion + 1, GetNowUnixMilliseconds());
            _current = published = new(snapshotVersion, linkedWorkerCount, linkedHttpWorkerCount);
            changed = _changed;
            _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        changed.TrySetResult();
        Log.StateChanged(_logger, published.SnapshotVersion, published.LinkedWorkerCount, published.LinkedHttpWorkerCount,
            scriptHostState, stopping);
    }

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
