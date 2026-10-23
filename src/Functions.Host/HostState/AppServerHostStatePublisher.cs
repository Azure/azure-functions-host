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
/// Failed requests use exponential backoff, interrupted by a newer snapshot. Successful snapshots are not periodically
/// resent; a successful HTTP response does not establish restart recovery or an exact applied-state acknowledgement.
/// Snapshot identity, not timestamp ordering, drives local deduplication.
/// When the root Host stops, the state manager withdraws capacity and the publisher makes one bounded final attempt.
/// </remarks>
internal sealed partial class AppServerHostStatePublisher(
    IRuntimeStateManager stateManager,
    IAppServerHostStateClient client,
    IOptionsMonitor<StandbyOptions> standbyOptions,
    TimeProvider timeProvider,
    ILoggerFactory loggerFactory) : BackgroundService
{
    internal const string LogCategory = "Host.AppServer.HostStatePublisher";

    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ShutdownPublishTimeout = TimeSpan.FromSeconds(2);

    private readonly IRuntimeStateManager _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
    private readonly IAppServerHostStateClient _client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly IOptionsMonitor<StandbyOptions> _standbyOptions = standbyOptions ?? throw new ArgumentNullException(nameof(standbyOptions));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ILogger _logger = loggerFactory?.CreateLogger(LogCategory) ?? throw new ArgumentNullException(nameof(loggerFactory));
    private RuntimeState? _lastPublishedSnapshot;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
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

            if (specialized)
            {
                await PublishFinalSnapshotAsync();
            }
        }
        catch (Exception exception)
        {
            // Publication, including its final attempt, must not fault the root Host's background service.
            Log.PublisherFailed(_logger, exception);
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
            cancellationToken.ThrowIfCancellationRequested();
            Task changed = _stateManager.GetChangeTask();
            RuntimeState state = _stateManager.Current;

            if (ReferenceEquals(_lastPublishedSnapshot, state))
            {
                await changed.WaitAsync(cancellationToken);
                continue;
            }

            if (await TryPublishAsync(state, cancellationToken))
            {
                retryDelay = InitialRetryDelay;
            }
            else
            {
                await WaitForChangeOrDelayAsync(changed, retryDelay, cancellationToken);
                retryDelay = GetNextRetryDelay(retryDelay);
            }
        }
    }

    // Retry the latest snapshot, not an HTTP handler's previously serialized request.
    private async Task WaitForChangeOrDelayAsync(Task changed, TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await changed.WaitAsync(delay, _timeProvider, cancellationToken);
        }
        catch (TimeoutException)
        {
            // The retry delay elapsed without a newer snapshot.
        }
    }

    private async Task<bool> TryPublishAsync(RuntimeState state, CancellationToken cancellationToken)
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
        RuntimeState state = _stateManager.Current;
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
        // EventId range is 1000-1099.
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
