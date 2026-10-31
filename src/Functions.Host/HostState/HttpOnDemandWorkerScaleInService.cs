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
/// Periodically publishes Host capacity and drains one grant-safe HTTP on-demand worker.
/// </summary>
internal sealed partial class HttpOnDemandWorkerScaleInService(
    IHttpWorkerScaleInManager scaleInManager,
    IRuntimeWorkerAdmissionGate runtimeAdmissionGate,
    HostStatePublicationCoordinator publicationCoordinator,
    TimeProvider timeProvider,
    IHostApplicationLifetime applicationLifetime,
    ILogger<HttpOnDemandWorkerScaleInService> logger) : BackgroundService
{
    internal static readonly TimeSpan AttemptInterval = TimeSpan.FromMinutes(10);

    private readonly IHostApplicationLifetime _applicationLifetime =
        applicationLifetime ?? throw new ArgumentNullException(nameof(applicationLifetime));

    private readonly ILogger<HttpOnDemandWorkerScaleInService> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    private readonly HostStatePublicationCoordinator _publicationCoordinator =
        publicationCoordinator ?? throw new ArgumentNullException(nameof(publicationCoordinator));

    private readonly IRuntimeWorkerAdmissionGate _runtimeAdmissionGate =
        runtimeAdmissionGate ?? throw new ArgumentNullException(nameof(runtimeAdmissionGate));

    private readonly IHttpWorkerScaleInManager _scaleInManager =
        scaleInManager ?? throw new ArgumentNullException(nameof(scaleInManager));

    private readonly TimeProvider _timeProvider =
        timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(AttemptInterval, _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            if (_runtimeAdmissionGate.IsClosed)
            {
                return;
            }

            try
            {
                await RunScaleInPassAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                Log.PassFailed(_logger, exception);
            }
        }
    }

    internal async Task RunScaleInPassAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_applicationLifetime.ApplicationStopping.IsCancellationRequested ||
            _runtimeAdmissionGate.IsClosed)
        {
            return;
        }

        (AppServerHostStatePublishResult currentResult, _) =
            await _publicationCoordinator.PublishCurrentCapacityFenceUntilStableAsync(cancellationToken);
        if (currentResult is not AppServerHostStatePublishResult.Applied)
        {
            Log.PublicationUnavailable(_logger, currentResult);
            return;
        }

        if (_applicationLifetime.ApplicationStopping.IsCancellationRequested ||
            _runtimeAdmissionGate.IsClosed)
        {
            return;
        }

        HttpWorkerScaleInReservation reservation = _scaleInManager.TryReserveWorker();
        if (reservation is null)
        {
            return;
        }

        AppServerHostStatePublishResult projectedResult;
        try
        {
            projectedResult = await _publicationCoordinator.PublishCapacityFenceAsync(
                reservation.ProjectedState,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CancelAndRestoreAsync(reservation, CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            await CancelAndRestoreAsync(reservation, CancellationToken.None);
            Log.PassFailed(_logger, exception);
            return;
        }

        if (projectedResult is not AppServerHostStatePublishResult.Applied)
        {
            await CancelAndRestoreAsync(reservation, CancellationToken.None);
            if (projectedResult is AppServerHostStatePublishResult.CapacityBlocked)
            {
                Log.CapacityBlocked(_logger, reservation.WorkerId, reservation.ProjectedState.HttpCapacity);
            }
            else
            {
                Log.PublicationUnavailable(_logger, projectedResult);
            }

            return;
        }

        if (_applicationLifetime.ApplicationStopping.IsCancellationRequested ||
            _runtimeAdmissionGate.IsClosed)
        {
            await CancelAndRestoreAsync(reservation, CancellationToken.None);
            return;
        }

        if (!_scaleInManager.TryCommitReservation(reservation, out Task<bool> drainCompletion) ||
            drainCompletion is null)
        {
            await CancelAndRestoreAsync(reservation, CancellationToken.None);
            return;
        }

        Log.WorkerSelected(_logger, reservation.WorkerId, reservation.ProjectedState.HttpCapacity);
        _ = ObserveDrainAsync(reservation.WorkerId, drainCompletion);
    }

    private async Task CancelAndRestoreAsync(
        HttpWorkerScaleInReservation reservation,
        CancellationToken cancellationToken)
    {
        bool canceled = _scaleInManager.CancelReservation(
            reservation,
            out bool invocationAdmissionRestored);
        if (_runtimeAdmissionGate.IsClosed ||
            (canceled && !invocationAdmissionRestored))
        {
            return;
        }

        try
        {
            await _publicationCoordinator.PublishCurrentCapacityFenceUntilStableAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            Log.RestoreFailed(_logger, exception);
        }
    }

    private async Task ObserveDrainAsync(string workerId, Task<bool> drainCompletion)
    {
        try
        {
            if (!await drainCompletion)
            {
                Log.DrainIncomplete(_logger, workerId);
            }
        }
        catch (Exception exception)
        {
            Log.DrainFailed(_logger, exception, workerId);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(1100, LogLevel.Information,
            "Selected HTTP on-demand worker {WorkerId} for drain after publishing projected capacity {HttpCapacity}.")]
        public static partial void WorkerSelected(ILogger logger, string workerId, long httpCapacity);

        [LoggerMessage(1101, LogLevel.Debug,
            "HTTP on-demand worker {WorkerId} was retained because projected capacity {HttpCapacity} does not cover Frontend slot obligations.")]
        public static partial void CapacityBlocked(ILogger logger, string workerId, long httpCapacity);

        [LoggerMessage(1102, LogLevel.Warning,
            "AppServer Host-state publication is unavailable for autonomous HTTP scale-in: {Result}.")]
        public static partial void PublicationUnavailable(ILogger logger, AppServerHostStatePublishResult result);

        [LoggerMessage(1103, LogLevel.Error, "Autonomous HTTP on-demand worker scale-in pass failed.")]
        public static partial void PassFailed(ILogger logger, Exception exception);

        [LoggerMessage(1104, LogLevel.Warning,
            "Failed to restore Host capacity after canceling an HTTP worker scale-in reservation.")]
        public static partial void RestoreFailed(ILogger logger, Exception exception);

        [LoggerMessage(1105, LogLevel.Warning,
            "The committed drain for HTTP on-demand worker {WorkerId} did not complete locally.")]
        public static partial void DrainIncomplete(ILogger logger, string workerId);

        [LoggerMessage(1106, LogLevel.Error,
            "The committed drain for HTTP on-demand worker {WorkerId} failed.")]
        public static partial void DrainFailed(ILogger logger, Exception exception, string workerId);
    }
}
