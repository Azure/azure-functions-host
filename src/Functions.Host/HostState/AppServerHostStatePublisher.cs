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

namespace Azure.Functions.Host.HostState;

/// <summary>
/// Publishes the latest Host HTTP capacity snapshot to AppServer after specialization.
/// </summary>
/// <remarks>
/// <para>
/// The first publish carries the current snapshot, normally zero capacity. Later publications carry the latest snapshot.
/// A failed publish is retried with exponential backoff, and a newer snapshot replaces the one being retried. An acknowledged
/// snapshot is never resent, so restoring state after an AppServer restart is AppServer's responsibility.
/// Snapshot identity drives local waits and deduplication; timestamps may be equal or move backward.
/// </para>
/// <para>
/// When the Host stops, the state manager records zero capacity and this publisher makes one bounded attempt to send it.
/// Publication failures are logged and never stop the Host.
/// </para>
/// </remarks>
internal sealed partial class AppServerHostStatePublisher : BackgroundService
{
    // Keep the Host-prefixed category so existing provider and customer log filters continue to apply.
    internal const string LogCategory = "Host.AppServer.HostStatePublisher";

    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ShutdownPublishTimeout = TimeSpan.FromSeconds(2);

    private readonly IComputeRuntimeStateManager _stateManager;
    private readonly IAppServerHostStateClient _client;
    private readonly IOptionsMonitor<StandbyOptions> _standbyOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private ComputeRuntimeState? _lastPublishedSnapshot;

    public AppServerHostStatePublisher(
        IComputeRuntimeStateManager stateManager,
        IAppServerHostStateClient client,
        IOptionsMonitor<StandbyOptions> standbyOptions,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
    {
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _standbyOptions = standbyOptions ?? throw new ArgumentNullException(nameof(standbyOptions));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = loggerFactory?.CreateLogger(LogCategory) ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        bool specialized = false;
        try
        {
            await WaitForSpecializationAsync(stoppingToken);
            specialized = true;
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

        if (specialized)
        {
            await PublishFinalSnapshotAsync();
        }
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
        TimeSpan retryDelay = InitialRetryDelay;

        while (true)
        {
            ComputeRuntimeState state = _stateManager.Current;

            if (await TryPublishAsync(state, cancellationToken))
            {
                // AppServer acknowledged this snapshot; publish again only when a different instance exists.
                retryDelay = InitialRetryDelay;
                await _stateManager.WaitForChangeAsync(state, cancellationToken);
            }
            else
            {
                await WaitForChangeOrDelayAsync(state, retryDelay, cancellationToken);
                retryDelay = GetNextRetryDelay(retryDelay);
            }
        }
    }

    private async Task WaitForChangeOrDelayAsync(ComputeRuntimeState state, TimeSpan delay, CancellationToken cancellationToken)
    {
        using CancellationTokenSource delaySource = new(delay, _timeProvider);
        using CancellationTokenSource waitSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, delaySource.Token);
        try
        {
            await _stateManager.WaitForChangeAsync(state, waitSource.Token);
        }
        catch (OperationCanceledException) when (delaySource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // The retry delay elapsed before a newer snapshot existed.
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
            Log.PublishFailed(_logger, exception, state.CreatedTime);
            return false;
        }

        switch ((int)statusCode)
        {
            case >= 200 and < 300:
                Log.Published(_logger, state.CreatedTime, state.HttpCapacity);
                _lastPublishedSnapshot = state;
                return true;
            case (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden or >= 500:
                Log.PublishRejectedTransient(_logger, state.CreatedTime, (int)statusCode);
                return false;
            default:
                Log.PublishRejected(_logger, state.CreatedTime, (int)statusCode);
                return false;
        }
    }

    private async Task PublishFinalSnapshotAsync()
    {
        ComputeRuntimeState state = _stateManager.Current;
        if (ReferenceEquals(_lastPublishedSnapshot, state))
        {
            return;
        }

        using CancellationTokenSource timeout = new(ShutdownPublishTimeout, _timeProvider);
        try
        {
            await TryPublishAsync(state, timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            Log.FinalPublishTimedOut(_logger, state.CreatedTime);
        }
    }

    private static TimeSpan GetNextRetryDelay(TimeSpan retryDelay)
        => retryDelay * 2 < MaxRetryDelay ? retryDelay * 2 : MaxRetryDelay;

    private static partial class Log
    {
        // EventId range is 1000-1099
        [LoggerMessage(1000, LogLevel.Information,
            "Published Host state to AppServer: created time {CreatedTime}, HTTP capacity {HttpCapacity}.")]
        public static partial void Published(ILogger logger, DateTimeOffset createdTime, long httpCapacity);

        [LoggerMessage(1001, LogLevel.Warning,
            "Failed to publish Host state snapshot created at {CreatedTime} to AppServer. The latest snapshot will be retried.")]
        public static partial void PublishFailed(ILogger logger, Exception exception, DateTimeOffset createdTime);

        [LoggerMessage(1002, LogLevel.Warning,
            "AppServer returned status code {StatusCode} for Host state snapshot created at {CreatedTime}. The latest snapshot will be retried.")]
        public static partial void PublishRejectedTransient(ILogger logger, DateTimeOffset createdTime, int statusCode);

        [LoggerMessage(1003, LogLevel.Error,
            "AppServer rejected Host state snapshot created at {CreatedTime} with status code {StatusCode}. The latest snapshot will be retried.")]
        public static partial void PublishRejected(ILogger logger, DateTimeOffset createdTime, int statusCode);

        [LoggerMessage(1004, LogLevel.Warning,
            "Timed out publishing the final Host state snapshot created at {CreatedTime} to AppServer during shutdown.")]
        public static partial void FinalPublishTimedOut(ILogger logger, DateTimeOffset createdTime);

        [LoggerMessage(1005, LogLevel.Error, "Host state publication to AppServer stopped unexpectedly.")]
        public static partial void PublisherFailed(ILogger logger, Exception exception);
    }
}
