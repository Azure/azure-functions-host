// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Rpc.Client;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Host.AppServer;

/// <summary>
/// Publishes the latest Host worker-count snapshot to AppServer after specialization.
/// </summary>
/// <remarks>
/// <para>
/// The first publish carries the current snapshot, normally zero counts. Each later snapshot is sent as soon as it exists.
/// A failed publish is retried with exponential backoff, and a newer snapshot replaces the one being retried. After a
/// successful publish, the same snapshot is resent on a heartbeat so AppServer recovers state after it restarts; AppServer
/// treats an identical replay as a no-op.
/// </para>
/// <para>
/// When the Host stops, the state manager publishes zero counts and this publisher makes one bounded attempt to send them.
/// Publication failures are logged and never stop the Host.
/// </para>
/// </remarks>
internal sealed partial class AppServerHostStatePublisher : BackgroundService
{
    // Host logging keeps only system-prefixed categories, so the ILogger<T> type-name category would be dropped.
    internal const string LogCategory = "Host.AppServer.HostStatePublisher";

    private static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultInitialRetryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan DefaultShutdownPublishTimeout = TimeSpan.FromSeconds(2);

    private readonly IComputeRuntimeStateManager _stateManager;
    private readonly IAppServerHostStateClient _client;
    private readonly IOptionsMonitor<StandbyOptions> _standbyOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly TimeSpan _heartbeatInterval;
    private readonly TimeSpan _initialRetryDelay;
    private readonly TimeSpan _shutdownPublishTimeout;
    private long? _lastPublishedVersion;
    private bool _specialized;

    public AppServerHostStatePublisher(
        IComputeRuntimeStateManager stateManager,
        IAppServerHostStateClient client,
        IOptionsMonitor<StandbyOptions> standbyOptions,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
        : this(stateManager, client, standbyOptions, timeProvider, loggerFactory,
            DefaultHeartbeatInterval, DefaultInitialRetryDelay, DefaultShutdownPublishTimeout)
    {
    }

    internal AppServerHostStatePublisher(
        IComputeRuntimeStateManager stateManager,
        IAppServerHostStateClient client,
        IOptionsMonitor<StandbyOptions> standbyOptions,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        TimeSpan heartbeatInterval,
        TimeSpan initialRetryDelay,
        TimeSpan shutdownPublishTimeout)
    {
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _standbyOptions = standbyOptions ?? throw new ArgumentNullException(nameof(standbyOptions));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = loggerFactory?.CreateLogger(LogCategory) ?? throw new ArgumentNullException(nameof(loggerFactory));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(heartbeatInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(initialRetryDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(shutdownPublishTimeout, TimeSpan.Zero);
        _heartbeatInterval = heartbeatInterval;
        _initialRetryDelay = initialRetryDelay;
        _shutdownPublishTimeout = shutdownPublishTimeout;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await WaitForSpecializationAsync(stoppingToken);
            _specialized = true;
            await PublishUntilStoppedAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            // BackgroundService failures stop the Host by default; publication must never do that.
            Log.PublisherFailed(_logger, exception);
            return;
        }

        await PublishFinalSnapshotAsync();
    }

    private async Task WaitForSpecializationAsync(CancellationToken cancellationToken)
    {
        if (!_standbyOptions.CurrentValue.InStandbyMode)
        {
            return;
        }

        TaskCompletionSource specialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable? registration = _standbyOptions.OnChange(options =>
        {
            if (!options.InStandbyMode)
            {
                specialized.TrySetResult();
            }
        });

        // Specialization can complete between the first check and the change registration.
        if (_standbyOptions.CurrentValue.InStandbyMode)
        {
            await specialized.Task.WaitAsync(cancellationToken);
        }
    }

    private async Task PublishUntilStoppedAsync(CancellationToken cancellationToken)
    {
        TimeSpan retryDelay = _initialRetryDelay;
        bool signingKeyUnavailableLogged = false;

        while (true)
        {
            ComputeRuntimeState state = _stateManager.Current;
            TimeSpan wait;

            if (!_client.HasSigningKey)
            {
                if (!signingKeyUnavailableLogged)
                {
                    Log.SigningKeyUnavailable(_logger);
                    signingKeyUnavailableLogged = true;
                }

                wait = retryDelay;
                retryDelay = GetNextRetryDelay(retryDelay);
            }
            else if (await TryPublishAsync(state, cancellationToken))
            {
                retryDelay = _initialRetryDelay;
                wait = _heartbeatInterval;
            }
            else
            {
                wait = retryDelay;
                retryDelay = GetNextRetryDelay(retryDelay);
            }

            await WaitForChangeOrDelayAsync(state.SnapshotVersion, wait, cancellationToken);
        }
    }

    private async Task WaitForChangeOrDelayAsync(long snapshotVersion, TimeSpan delay, CancellationToken cancellationToken)
    {
        using CancellationTokenSource waitSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<ComputeRuntimeState> changed = _stateManager.WaitForChangeAsync(snapshotVersion, waitSource.Token);
        Task completed;
        try
        {
            completed = await Task.WhenAny(changed, Task.Delay(delay, _timeProvider, waitSource.Token));
        }
        finally
        {
            await waitSource.CancelAsync();
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (completed == changed)
        {
            // Surface a failed wait instead of republishing in a tight loop.
            await changed;
        }
    }

    private async Task<bool> TryPublishAsync(ComputeRuntimeState state, CancellationToken cancellationToken)
    {
        HttpStatusCode statusCode;
        try
        {
            statusCode = await _client.PublishAsync(state, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log.PublishFailed(_logger, exception, state.SnapshotVersion);
            return false;
        }

        switch ((int)statusCode)
        {
            case >= 200 and < 300:
                if (_lastPublishedVersion != state.SnapshotVersion)
                {
                    Log.Published(_logger, state.SnapshotVersion, state.LinkedWorkerCount, state.LinkedHttpWorkerCount);
                    _lastPublishedVersion = state.SnapshotVersion;
                }

                return true;
            case (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden or >= 500:
                Log.PublishRejectedTransient(_logger, state.SnapshotVersion, (int)statusCode);
                return false;
            default:
                Log.PublishRejected(_logger, state.SnapshotVersion, (int)statusCode);
                return false;
        }
    }

    private async Task PublishFinalSnapshotAsync()
    {
        ComputeRuntimeState state = _stateManager.Current;
        if (!_specialized || !_client.HasSigningKey || _lastPublishedVersion == state.SnapshotVersion)
        {
            return;
        }

        using CancellationTokenSource timeout = new(_shutdownPublishTimeout, _timeProvider);
        try
        {
            await TryPublishAsync(state, timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            Log.FinalPublishTimedOut(_logger, state.SnapshotVersion);
        }
    }

    private TimeSpan GetNextRetryDelay(TimeSpan retryDelay)
        => retryDelay * 2 < _heartbeatInterval ? retryDelay * 2 : _heartbeatInterval;

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Information,
            "Published Host state to AppServer: snapshot version {SnapshotVersion}, {LinkedWorkerCount} linked worker(s), " +
            "{LinkedHttpWorkerCount} linked HTTP worker(s).")]
        public static partial void Published(ILogger logger, long snapshotVersion, int linkedWorkerCount, int linkedHttpWorkerCount);

        [LoggerMessage(1, LogLevel.Warning,
            "Failed to publish Host state snapshot version {SnapshotVersion} to AppServer. The latest snapshot will be retried.")]
        public static partial void PublishFailed(ILogger logger, Exception exception, long snapshotVersion);

        [LoggerMessage(2, LogLevel.Warning,
            "AppServer returned status code {StatusCode} for Host state snapshot version {SnapshotVersion}. The latest snapshot will be retried.")]
        public static partial void PublishRejectedTransient(ILogger logger, long snapshotVersion, int statusCode);

        [LoggerMessage(3, LogLevel.Error,
            "AppServer rejected Host state snapshot version {SnapshotVersion} with status code {StatusCode}. The latest snapshot will be retried.")]
        public static partial void PublishRejected(ILogger logger, long snapshotVersion, int statusCode);

        [LoggerMessage(4, LogLevel.Warning,
            "Host state is not published to AppServer because WEBSITE_AUTH_ENCRYPTION_KEY is not available. Publication starts once it is.")]
        public static partial void SigningKeyUnavailable(ILogger logger);

        [LoggerMessage(5, LogLevel.Warning,
            "Timed out publishing the final Host state snapshot version {SnapshotVersion} to AppServer during shutdown.")]
        public static partial void FinalPublishTimedOut(ILogger logger, long snapshotVersion);

        [LoggerMessage(6, LogLevel.Error, "Host state publication to AppServer stopped unexpectedly.")]
        public static partial void PublisherFailed(ILogger logger, Exception exception);
    }
}
