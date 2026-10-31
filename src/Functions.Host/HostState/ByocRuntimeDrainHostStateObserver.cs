// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Rpc.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Host.HostState;

/// <summary>
/// Publishes shared Host-state changes while the BYOC Runtime drain owns admission.
/// </summary>
internal sealed partial class ByocRuntimeDrainHostStateObserver(
    IHostStateCoordinator hostStateCoordinator,
    IRuntimeWorkerAdmissionGate runtimeAdmissionGate,
    HostStatePublicationCoordinator publicationCoordinator,
    TimeProvider timeProvider,
    ILogger<ByocRuntimeDrainHostStateObserver> logger) : BackgroundService
{
    private static readonly TimeSpan InitialRetryDelay =
        TimeSpan.FromMilliseconds(250);

    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan ShutdownPublishTimeout =
        TimeSpan.FromSeconds(2);

    private readonly IHostStateCoordinator _hostStateCoordinator =
        hostStateCoordinator ?? throw new ArgumentNullException(nameof(hostStateCoordinator));

    private readonly ILogger<ByocRuntimeDrainHostStateObserver> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    private readonly HostStatePublicationCoordinator _publicationCoordinator =
        publicationCoordinator ?? throw new ArgumentNullException(nameof(publicationCoordinator));

    private readonly IRuntimeWorkerAdmissionGate _runtimeAdmissionGate =
        runtimeAdmissionGate ?? throw new ArgumentNullException(nameof(runtimeAdmissionGate));

    private readonly TimeProvider _timeProvider =
        timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    private long _publishedVersion = -1;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ObserveAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch (Exception exception)
        {
            Log.ObserverFailed(_logger, exception);
            return;
        }

        await PublishFinalSnapshotAsync();
    }

    private async Task ObserveAsync(CancellationToken cancellationToken)
    {
        TimeSpan retryDelay = InitialRetryDelay;
        while (true)
        {
            HostStateSnapshot state = _hostStateCoordinator.Current;
            if (!_runtimeAdmissionGate.IsClosed)
            {
                await _hostStateCoordinator.WaitForChangeAsync(
                    state.Version,
                    cancellationToken);
                continue;
            }

            (bool applied, HostStateSnapshot publishedState) =
                await TryPublishAsync(cancellationToken);
            if (applied)
            {
                _publishedVersion = publishedState.Version;
                retryDelay = InitialRetryDelay;
                HostStateSnapshot current = _hostStateCoordinator.Current;
                if (current.Version == publishedState.Version)
                {
                    await _hostStateCoordinator.WaitForChangeAsync(
                        publishedState.Version,
                        cancellationToken);
                }
            }
            else
            {
                await WaitForChangeOrDelayAsync(
                    publishedState.Version,
                    retryDelay,
                    cancellationToken);
                retryDelay = GetNextRetryDelay(retryDelay);
            }
        }
    }

    private async Task<(bool Applied, HostStateSnapshot State)> TryPublishAsync(
        CancellationToken cancellationToken)
    {
        HostStateSnapshot initialState = _hostStateCoordinator.Current;
        try
        {
            (AppServerHostStatePublishResult result, HostStateSnapshot state) =
                await _publicationCoordinator
                    .PublishCurrentAuthoritativeUntilStableAsync(cancellationToken);
            if (result is AppServerHostStatePublishResult.Applied)
            {
                Log.Published(
                    _logger,
                    state.CreatedTime,
                    state.LinkedWorkerCount);
                return (true, state);
            }

            Log.PublishRejected(
                _logger,
                state.CreatedTime,
                state.LinkedWorkerCount,
                result);
            return (false, state);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log.PublishFailed(
                _logger,
                exception,
                initialState.CreatedTime,
                initialState.LinkedWorkerCount);
            return (false, initialState);
        }
    }

    private async Task WaitForChangeOrDelayAsync(
        long version,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource delaySource =
            new(delay, _timeProvider);
        using CancellationTokenSource waitSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                delaySource.Token);
        try
        {
            await _hostStateCoordinator.WaitForChangeAsync(
                version,
                waitSource.Token);
        }
        catch (OperationCanceledException) when (
            delaySource.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task PublishFinalSnapshotAsync()
    {
        try
        {
            HostStateSnapshot state = _hostStateCoordinator.Current;
            if (!_runtimeAdmissionGate.IsClosed ||
                state.Version == _publishedVersion)
            {
                return;
            }

            using CancellationTokenSource timeout =
                new(ShutdownPublishTimeout, _timeProvider);
            await TryPublishAsync(timeout.Token);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (OperationCanceledException)
        {
            Log.FinalPublishTimedOut(_logger);
        }
    }

    private static TimeSpan GetNextRetryDelay(TimeSpan retryDelay)
        => retryDelay * 2 < MaxRetryDelay
            ? retryDelay * 2
            : MaxRetryDelay;

    private static partial class Log
    {
        [LoggerMessage(
            1200,
            LogLevel.Information,
            "Published BYOC Runtime drain Host state created at {CreatedTime} with {LinkedWorkerCount} linked Workers.")]
        public static partial void Published(
            ILogger logger,
            DateTimeOffset createdTime,
            int linkedWorkerCount);

        [LoggerMessage(
            1201,
            LogLevel.Warning,
            "Failed to publish BYOC Runtime drain Host state created at {CreatedTime} with {LinkedWorkerCount} linked Workers.")]
        public static partial void PublishFailed(
            ILogger logger,
            Exception exception,
            DateTimeOffset createdTime,
            int linkedWorkerCount);

        [LoggerMessage(
            1202,
            LogLevel.Warning,
            "AppServer did not apply BYOC Runtime drain Host state created at {CreatedTime} with {LinkedWorkerCount} linked Workers: {Result}.")]
        public static partial void PublishRejected(
            ILogger logger,
            DateTimeOffset createdTime,
            int linkedWorkerCount,
            AppServerHostStatePublishResult result);

        [LoggerMessage(
            1203,
            LogLevel.Error,
            "BYOC Runtime drain Host-state observation stopped unexpectedly.")]
        public static partial void ObserverFailed(
            ILogger logger,
            Exception exception);

        [LoggerMessage(
            1204,
            LogLevel.Warning,
            "Timed out publishing the final BYOC Runtime drain Host state during shutdown.")]
        public static partial void FinalPublishTimedOut(ILogger logger);
    }
}
