// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Rpc.Client;

internal sealed partial class RuntimeDrainManager : IRuntimeDrainManager, IAsyncDisposable
{
    private static readonly TimeSpan[] NonHttpBatchOffsets =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(40),
    ];

    private static readonly TimeSpan HttpSelectionOffset = TimeSpan.FromSeconds(35);

    private readonly ILogger<RuntimeDrainManager> _logger;
    private readonly Lock _operationLock = new();
    private readonly CancellationTokenSource _shutdownSource;
    private readonly TimeProvider _timeProvider;
    private readonly IRuntimeDrainWorkerManager _workerManager;
    private DrainOperation _operation;
    private Task _scheduleTask = Task.CompletedTask;
    private Task _disposeTask;

    public RuntimeDrainManager(
        IRuntimeDrainWorkerManager workerManager,
        IHostApplicationLifetime applicationLifetime,
        TimeProvider timeProvider,
        ILogger<RuntimeDrainManager> logger)
    {
        _workerManager = workerManager ?? throw new ArgumentNullException(nameof(workerManager));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(applicationLifetime);
        _shutdownSource = CancellationTokenSource.CreateLinkedTokenSource(applicationLifetime.ApplicationStopping);
    }

    internal Task ScheduleCompletion
    {
        get
        {
            lock (_operationLock)
            {
                return _scheduleTask;
            }
        }
    }

    public RuntimeDrainReceipt Accept(RuntimeDrainMode drainMode)
    {
        if (!Enum.IsDefined(drainMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(drainMode),
                drainMode,
                "The Runtime drain mode is unsupported.");
        }

        lock (_operationLock)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
            if (_operation is null)
            {
                RuntimeDrainStart start = _workerManager.CloseAdmission();
                DrainOperation operation = new(
                    drainMode,
                    _timeProvider.GetUtcNow(),
                    _timeProvider.GetTimestamp(),
                    start,
                    _shutdownSource.Token);
                _operation = operation;

                if (drainMode is RuntimeDrainMode.Immediate)
                {
                    SelectAllRemainingLocked(operation);
                }
                else
                {
                    SelectNonHttpBatchLocked(operation, batchIndex: 0);
                    if (!operation.SelectionFinalized)
                    {
                        _scheduleTask = RunPhasedScheduleAsync(operation);
                    }
                }

                operation.Failure?.Throw();
                Log.Accepted(_logger, drainMode, operation.AcceptedAtUtc);
                return operation.CreateReceipt();
            }

            _operation.Failure?.Throw();
            if (_operation.ActiveMode == drainMode)
            {
                return _operation.CreateReceipt();
            }

            if (_operation.ActiveMode is RuntimeDrainMode.Immediate)
            {
                throw new RuntimeDrainConflictException(
                    "The Runtime is already draining with the Immediate mode.");
            }

            _operation.ActiveMode = RuntimeDrainMode.Immediate;
            _operation.ScheduleCancellation.Cancel();
            SelectAllRemainingLocked(_operation);
            Log.Escalated(_logger, _operation.AcceptedAtUtc);

            return _operation.CreateReceipt();
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (_operationLock)
        {
            return new(_disposeTask ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        await _shutdownSource.CancelAsync().ConfigureAwait(false);
        await _scheduleTask.ConfigureAwait(false);
        _operation?.ScheduleCancellation.Dispose();
        _shutdownSource.Dispose();
    }

    private async Task RunPhasedScheduleAsync(DrainOperation operation)
    {
        try
        {
            for (int batchIndex = 1; batchIndex < NonHttpBatchOffsets.Length - 1; batchIndex++)
            {
                await DelayFromStartAsync(
                    operation,
                    NonHttpBatchOffsets[batchIndex],
                    operation.ScheduleCancellation.Token);
                lock (_operationLock)
                {
                    if (!CanRunPhasedSelectionLocked(operation))
                    {
                        return;
                    }

                    SelectNonHttpBatchLocked(operation, batchIndex);
                }
            }

            await DelayFromStartAsync(
                operation,
                HttpSelectionOffset,
                operation.ScheduleCancellation.Token);
            lock (_operationLock)
            {
                if (!CanRunPhasedSelectionLocked(operation))
                {
                    return;
                }

                SelectWorkersLocked(operation, operation.Start.HttpWorkerIds);
            }

            await DelayFromStartAsync(
                operation,
                NonHttpBatchOffsets[^1],
                operation.ScheduleCancellation.Token);
            lock (_operationLock)
            {
                if (!CanRunPhasedSelectionLocked(operation))
                {
                    return;
                }

                SelectNonHttpBatchLocked(operation, NonHttpBatchOffsets.Length - 1);
            }
        }
        catch (OperationCanceledException) when (operation.ScheduleCancellation.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (_shutdownSource.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            lock (_operationLock)
            {
                if (operation.ActiveMode is RuntimeDrainMode.Phased && !operation.SelectionFinalized)
                {
                    operation.Failure ??= ExceptionDispatchInfo.Capture(exception);
                }
            }

            Log.ScheduleFailed(_logger, exception);
        }
    }

    private bool CanRunPhasedSelectionLocked(DrainOperation operation)
        => ReferenceEquals(_operation, operation) &&
            !operation.ScheduleCancellation.IsCancellationRequested &&
            operation.Failure is null &&
            operation.ActiveMode is RuntimeDrainMode.Phased &&
            !operation.SelectionFinalized;

    private void SelectNonHttpBatchLocked(DrainOperation operation, int batchIndex)
    {
        int workerCount = operation.Start.NonHttpWorkerIds.Count;
        int baseBatchSize = workerCount / NonHttpBatchOffsets.Length;
        int largerBatchCount = workerCount % NonHttpBatchOffsets.Length;
        int startIndex =
            (batchIndex * baseBatchSize) +
            Math.Min(batchIndex, largerBatchCount);
        int batchSize =
            baseBatchSize +
            (batchIndex < largerBatchCount ? 1 : 0);
        IReadOnlyList<string> workerIds = operation.Start.NonHttpWorkerIds
            .Skip(startIndex)
            .Take(batchSize)
            .ToArray();
        SelectWorkersLocked(operation, workerIds);
    }

    private void SelectAllRemainingLocked(DrainOperation operation)
    {
        string[] remainingWorkerIds = operation.AllWorkerIds
            .Where(workerId => !operation.SelectedWorkerIds.Contains(workerId))
            .ToArray();
        SelectWorkersLocked(operation, remainingWorkerIds);
    }

    private void SelectWorkersLocked(
        DrainOperation operation,
        IReadOnlyList<string> workerIds)
    {
        try
        {
            _workerManager.SelectWorkers(workerIds);
        }
        catch (Exception exception)
        {
            // Selection can partially drain a batch; retries must not repeat it or reset existing deadlines.
            operation.Failure ??= ExceptionDispatchInfo.Capture(exception);
            throw;
        }

        operation.SelectedWorkerIds.UnionWith(workerIds);

        if (operation.SelectedWorkerIds.Count == operation.AllWorkerIds.Count)
        {
            operation.SelectionFinalized = true;
            operation.ScheduleCancellation.Cancel();
        }
    }

    private async Task DelayFromStartAsync(
        DrainOperation operation,
        TimeSpan offset,
        CancellationToken cancellationToken)
    {
        TimeSpan remaining = offset - _timeProvider.GetElapsedTime(
            operation.AcceptedTimestamp,
            _timeProvider.GetTimestamp());
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, _timeProvider, cancellationToken);
        }
    }

    private sealed class DrainOperation
    {
        public DrainOperation(
            RuntimeDrainMode activeMode,
            DateTimeOffset acceptedAtUtc,
            long acceptedTimestamp,
            RuntimeDrainStart start,
            CancellationToken shutdownToken)
        {
            ActiveMode = activeMode;
            AcceptedAtUtc = acceptedAtUtc;
            AcceptedTimestamp = acceptedTimestamp;
            Start = start;
            AllWorkerIds = [.. start.NonHttpWorkerIds, .. start.HttpWorkerIds];
            ScheduleCancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
        }

        public DateTimeOffset AcceptedAtUtc { get; }

        public long AcceptedTimestamp { get; }

        public RuntimeDrainMode ActiveMode { get; set; }

        public ExceptionDispatchInfo Failure { get; set; }

        public IReadOnlyList<string> AllWorkerIds { get; }

        public CancellationTokenSource ScheduleCancellation { get; }

        public HashSet<string> SelectedWorkerIds { get; } = new(StringComparer.Ordinal);

        public bool SelectionFinalized { get; set; }

        public RuntimeDrainStart Start { get; }

        public RuntimeDrainReceipt CreateReceipt() => new(ActiveMode, AcceptedAtUtc);
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Information,
            "Accepted Runtime drain with {DrainMode} at {AcceptedAtUtc}.")]
        public static partial void Accepted(
            ILogger logger,
            RuntimeDrainMode drainMode,
            DateTimeOffset acceptedAtUtc);

        [LoggerMessage(1, LogLevel.Information,
            "Escalated Runtime drain to Immediate; original acceptance was {AcceptedAtUtc}.")]
        public static partial void Escalated(
            ILogger logger,
            DateTimeOffset acceptedAtUtc);

        [LoggerMessage(2, LogLevel.Error, "Runtime phased drain schedule failed.")]
        public static partial void ScheduleFailed(
            ILogger logger,
            Exception exception);
    }
}
