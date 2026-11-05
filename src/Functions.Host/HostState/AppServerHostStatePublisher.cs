// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Rpc.Client;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;
using Polly.Timeout;

namespace Azure.Functions.Host.HostState;

/// <summary>
/// Publishes the latest Host HTTP capacity snapshot to AppServer after specialization.
/// </summary>
/// <remarks>
/// Failed requests use exponential backoff, interrupted by a newer snapshot. Successful snapshots are not periodically
/// resent; a successful HTTP response does not establish restart recovery or an exact applied-state acknowledgement.
/// Authentication rejections are not retried until a different runtime snapshot exists.
/// Snapshot identity, not timestamp ordering, drives local deduplication.
/// After specialization, normal root Host shutdown permits one bounded final attempt using the current snapshot.
/// Unexpected publisher failures stop publication without another attempt or changing the borrowed state.
/// Fatal exceptions are not handled as recoverable publication failures.
/// </remarks>
internal sealed partial class AppServerHostStatePublisher(
    IRuntimeStateManager stateManager,
    IAppServerHostStateClient client,
    IOptionsMonitor<StandbyOptions> standbyOptions,
    TimeProvider timeProvider,
    IReadOnlyPolicyRegistry<string> policyRegistry,
    ILoggerFactory loggerFactory) : BackgroundService, IAppServerHostStatePublisher
{
    internal const string PublicationPolicyName = "AppServerHostStatePublication";
    internal const string ChangeTaskKey = "AppServerHostStateChange";
    internal const string CancellationTokenKey = "AppServerHostStateCancellation";
    private const string LogCategory = "Host.AppServer.HostStatePublisher";

    private static readonly TimeSpan FinalPublishTimeout = TimeSpan.FromSeconds(2);

    private readonly IRuntimeStateManager _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
    private readonly IAppServerHostStateClient _client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly IOptionsMonitor<StandbyOptions> _standbyOptions = standbyOptions ?? throw new ArgumentNullException(nameof(standbyOptions));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ILogger _logger = loggerFactory?.CreateLogger(LogCategory) ?? throw new ArgumentNullException(nameof(loggerFactory));
    private readonly IAsyncPolicy<PublicationOutcome> _publicationPolicy =
        (policyRegistry ?? throw new ArgumentNullException(nameof(policyRegistry))).Get<IAsyncPolicy<PublicationOutcome>>(PublicationPolicyName);

    private readonly SemaphoreSlim _publicationGate = new(1, 1);
    private RuntimeState? _lastCompletedSnapshot;

    internal enum PublicationOutcome
    {
        Published,
        Retry,
        Rejected,
        Unchanged,
    }

    /// <inheritdoc />
    public async Task PublishCurrentAsync(CancellationToken cancellationToken)
    {
        await WaitForSpecializationAsync(cancellationToken);
        Context context = new() { [CancellationTokenKey] = cancellationToken };
        PublicationOutcome outcome = await _publicationPolicy.ExecuteAsync(async (retryContext, token) =>
        {
            await _publicationGate.WaitAsync(token);
            try
            {
                // Sample after taking the publication gate so an older background send cannot overtake withdrawal.
                Task changed = _stateManager.GetChangeTask();
                RuntimeState state = await _stateManager.RefreshAsync(token);
                retryContext[ChangeTaskKey] = changed;
                PublicationOutcome result = await TryPublishAsync(state, token);
                if (result is not PublicationOutcome.Retry)
                {
                    _lastCompletedSnapshot = state;
                }

                return result;
            }
            finally
            {
                _publicationGate.Release();
            }
        }, context, cancellationToken);

        if (outcome is not PublicationOutcome.Published)
        {
            throw new HttpRequestException("AppServer rejected the capacity publication required for HTTP worker scale-in.");
        }
    }

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
            catch (OperationCanceledException exception) when (stoppingToken.IsCancellationRequested && !Utility.IsFatal(exception))
            {
            }

            // Only normal shutdown permits a final attempt, never an unexpected publication-loop failure.
            if (specialized)
            {
                await PublishFinalSnapshotAsync();
            }
        }
        catch (Exception exception) when (!Utility.IsFatal(exception))
        {
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
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Context context = new()
            {
                [CancellationTokenKey] = cancellationToken,
            };
            await _publicationPolicy.ExecuteAsync(PublishCurrentSnapshotAsync, context, cancellationToken);
        }
    }

    private async Task<PublicationOutcome> PublishCurrentSnapshotAsync(Context context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Each retry samples again; the captured signal also lets a newer snapshot interrupt the retry delay.
        Task changed;
        await _publicationGate.WaitAsync(cancellationToken);
        try
        {
            changed = _stateManager.GetChangeTask();
            RuntimeState state = _stateManager.Current;
            context[ChangeTaskKey] = changed;
            if (!ReferenceEquals(_lastCompletedSnapshot, state))
            {
                PublicationOutcome outcome = await TryPublishAsync(state, cancellationToken);
                if (outcome is not PublicationOutcome.Retry)
                {
                    _lastCompletedSnapshot = state;
                }

                return outcome;
            }
        }
        finally
        {
            _publicationGate.Release();
        }

        await changed.WaitAsync(cancellationToken);
        return PublicationOutcome.Unchanged;
    }

    private async Task<PublicationOutcome> TryPublishAsync(RuntimeState state, CancellationToken cancellationToken)
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
        catch (Exception exception) when (!Utility.IsFatal(exception)
            && exception is HttpRequestException or OperationCanceledException or TimeoutRejectedException)
        {
            Log.PublishFailed(_logger, exception, state.CreatedTime);
            return PublicationOutcome.Retry;
        }

        switch ((int)statusCode)
        {
            case >= 200 and < 300:
                Log.Published(_logger, state.CreatedTime, state.HttpCapacity, state.WorkerCount);
                return PublicationOutcome.Published;
            case (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden:
                Log.PublishRejected(_logger, state.CreatedTime, (int)statusCode);
                return PublicationOutcome.Rejected;
            case >= 500:
                Log.PublishRejectedTransient(_logger, state.CreatedTime, (int)statusCode);
                return PublicationOutcome.Retry;
            default:
                Log.PublishRejected(_logger, state.CreatedTime, (int)statusCode);
                return PublicationOutcome.Retry;
        }
    }

    private async Task PublishFinalSnapshotAsync()
    {
        RuntimeState current = _stateManager.Current;
        if (ReferenceEquals(_lastCompletedSnapshot, current))
        {
            return;
        }

        using CancellationTokenSource timeout = new(FinalPublishTimeout, _timeProvider);
        RuntimeState state = current;
        try
        {
            await _publicationGate.WaitAsync(timeout.Token);
            try
            {
                state = _stateManager.Current;
                if (!ReferenceEquals(_lastCompletedSnapshot, state))
                {
                    await TryPublishAsync(state, timeout.Token);
                }
            }
            finally
            {
                _publicationGate.Release();
            }
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested && !Utility.IsFatal(exception))
        {
            Log.FinalPublishTimedOut(_logger, state.CreatedTime);
        }
    }

    private static partial class Log
    {
        // EventId range is 1000-1099.
        [LoggerMessage(1000, LogLevel.Information,
            "Published Host state to AppServer: created time {CreatedTime}, HTTP capacity {HttpCapacity}, linked worker count {LinkedWorkerCount}.")]
        public static partial void Published(ILogger logger, DateTimeOffset createdTime, long httpCapacity, int linkedWorkerCount);

        [LoggerMessage(1001, LogLevel.Warning,
            "Failed to publish Host state snapshot created at {CreatedTime} to AppServer.")]
        public static partial void PublishFailed(ILogger logger, Exception exception, DateTimeOffset createdTime);

        [LoggerMessage(1002, LogLevel.Warning,
            "AppServer returned status code {StatusCode} for Host state snapshot created at {CreatedTime}.")]
        public static partial void PublishRejectedTransient(ILogger logger, DateTimeOffset createdTime, int statusCode);

        [LoggerMessage(1003, LogLevel.Error,
            "AppServer rejected Host state snapshot created at {CreatedTime} with status code {StatusCode}.")]
        public static partial void PublishRejected(ILogger logger, DateTimeOffset createdTime, int statusCode);

        [LoggerMessage(1004, LogLevel.Warning,
            "Timed out publishing the final Host state snapshot created at {CreatedTime} to AppServer.")]
        public static partial void FinalPublishTimedOut(ILogger logger, DateTimeOffset createdTime);

        [LoggerMessage(1005, LogLevel.Error, "Host state publication to AppServer stopped unexpectedly.")]
        public static partial void PublisherFailed(ILogger logger, Exception exception);
    }
}
