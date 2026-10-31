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
/// Publishes Host capacity and irreversibly drains grant-safe idle HTTP on-demand workers.
/// </summary>
internal sealed partial class HttpOnDemandWorkerScaleInService(
    IHttpWorkerScaleInManager scaleInManager,
    AppServerHostStateClient appServerClient,
    TimeProvider timeProvider,
    IHostApplicationLifetime applicationLifetime,
    ILogger<HttpOnDemandWorkerScaleInService> logger) : BackgroundService
{
    internal static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);
    internal static readonly TimeSpan IdleDuration = TimeSpan.FromMinutes(10);
    private const int MaxStablePublishAttempts = 3;

    private readonly AppServerHostStateClient _appServerClient =
        appServerClient ?? throw new ArgumentNullException(nameof(appServerClient));

    private readonly IHostApplicationLifetime _applicationLifetime =
        applicationLifetime ?? throw new ArgumentNullException(nameof(applicationLifetime));

    private readonly ILogger<HttpOnDemandWorkerScaleInService> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    private readonly IHttpWorkerScaleInManager _scaleInManager =
        scaleInManager ?? throw new ArgumentNullException(nameof(scaleInManager));

    private readonly TimeProvider _timeProvider =
        timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            long waitVersion = _scaleInManager.Version;
            try
            {
                waitVersion = await RunScaleInPassAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                Log.PassFailed(_logger, exception);
            }

            await WaitForChangeOrIntervalAsync(waitVersion, stoppingToken);
        }
    }

    internal async Task<long> RunScaleInPassAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_applicationLifetime.ApplicationStopping.IsCancellationRequested)
        {
            return _scaleInManager.Version;
        }

        (AppServerHostStatePublishResult currentResult, HttpWorkerScaleInState publishedState) =
            await PublishCurrentUntilStableAsync(cancellationToken);
        if (currentResult is not AppServerHostStatePublishResult.Applied)
        {
            Log.PublicationUnavailable(_logger, currentResult);
            return publishedState.Version;
        }

        if (_applicationLifetime.ApplicationStopping.IsCancellationRequested)
        {
            return publishedState.Version;
        }

        HttpWorkerScaleInReservation reservation = _scaleInManager.TryReserveIdleWorker(IdleDuration);
        if (reservation is null)
        {
            return publishedState.Version;
        }

        AppServerHostStatePublishResult projectedResult;
        try
        {
            projectedResult = await _appServerClient.PublishAsync(reservation.ProjectedState, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CancelAndRestoreAsync(reservation, CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            (_, HttpWorkerScaleInState restoredState) =
                await CancelAndRestoreAsync(reservation, CancellationToken.None);
            Log.PassFailed(_logger, exception);
            return restoredState.Version;
        }

        if (projectedResult is not AppServerHostStatePublishResult.Applied)
        {
            (_, HttpWorkerScaleInState restoredState) =
                await CancelAndRestoreAsync(reservation, CancellationToken.None);
            if (projectedResult is AppServerHostStatePublishResult.CapacityBlocked)
            {
                Log.CapacityBlocked(_logger, reservation.WorkerId, reservation.ProjectedState.HttpCapacity);
            }
            else
            {
                Log.PublicationUnavailable(_logger, projectedResult);
            }

            return restoredState.Version;
        }

        if (_applicationLifetime.ApplicationStopping.IsCancellationRequested)
        {
            (_, HttpWorkerScaleInState restoredState) =
                await CancelAndRestoreAsync(reservation, CancellationToken.None);
            return restoredState.Version;
        }

        if (!_scaleInManager.TryCommitReservation(reservation, out Task<bool> drainCompletion) ||
            drainCompletion is null)
        {
            (_, HttpWorkerScaleInState restoredState) =
                await CancelAndRestoreAsync(reservation, CancellationToken.None);
            return restoredState.Version;
        }

        Log.WorkerSelected(_logger, reservation.WorkerId, reservation.ProjectedState.HttpCapacity);
        _ = ObserveDrainAsync(reservation.WorkerId, drainCompletion);

        return reservation.ProjectedState.Version;
    }

    private async Task<(AppServerHostStatePublishResult Result, HttpWorkerScaleInState State)> CancelAndRestoreAsync(
        HttpWorkerScaleInReservation reservation,
        CancellationToken cancellationToken)
    {
        _scaleInManager.CancelReservation(reservation);
        return await PublishCurrentBestEffortAsync(cancellationToken);
    }

    private async Task<(AppServerHostStatePublishResult Result, HttpWorkerScaleInState State)> PublishCurrentBestEffortAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await PublishCurrentUntilStableAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            Log.RestoreFailed(_logger, exception);
            return (AppServerHostStatePublishResult.TransientFailure, _scaleInManager.Current);
        }
    }

    private async Task<(AppServerHostStatePublishResult Result, HttpWorkerScaleInState State)> PublishCurrentUntilStableAsync(
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < MaxStablePublishAttempts; attempt++)
        {
            HttpWorkerScaleInState state = _scaleInManager.Current;
            AppServerHostStatePublishResult result =
                await _appServerClient.PublishAsync(state, cancellationToken);
            if (result is not AppServerHostStatePublishResult.Applied ||
                _scaleInManager.Version == state.Version)
            {
                return (result, state);
            }
        }

        return (AppServerHostStatePublishResult.TransientFailure, _scaleInManager.Current);
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

    private async Task WaitForChangeOrIntervalAsync(long observedVersion, CancellationToken cancellationToken)
    {
        using CancellationTokenSource intervalSource = new(CheckInterval, _timeProvider);
        using CancellationTokenSource waitSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, intervalSource.Token);
        try
        {
            await _scaleInManager.WaitForChangeAsync(observedVersion, waitSource.Token);
        }
        catch (OperationCanceledException) when (intervalSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static partial class Log
    {
        [LoggerMessage(1100, LogLevel.Information,
            "Selected idle HTTP on-demand worker {WorkerId} for drain after publishing projected capacity {HttpCapacity}.")]
        public static partial void WorkerSelected(ILogger logger, string workerId, long httpCapacity);

        [LoggerMessage(1101, LogLevel.Debug,
            "Idle HTTP worker {WorkerId} was retained because projected capacity {HttpCapacity} does not cover Frontend slot obligations.")]
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
