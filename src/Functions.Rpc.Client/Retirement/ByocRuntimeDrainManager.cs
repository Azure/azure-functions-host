// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Rpc.Client;

internal sealed partial class ByocRuntimeDrainManager : IByocRuntimeDrainManager
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

    private readonly ILogger<ByocRuntimeDrainManager> _logger;
    private readonly Lock _operationLock = new();
    private readonly TimeProvider _timeProvider;
    private readonly IByocRuntimeDrainWorkerManager _workerManager;
    private DrainOperation _operation;

    public ByocRuntimeDrainManager(
        IByocRuntimeDrainWorkerManager workerManager,
        TimeProvider timeProvider,
        ILogger<ByocRuntimeDrainManager> logger)
    {
        _workerManager = workerManager ?? throw new ArgumentNullException(nameof(workerManager));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ByocRuntimeDrainReceipt Accept(ByocDrainModel drainModel)
    {
        if (!Enum.IsDefined(drainModel))
        {
            throw new ArgumentOutOfRangeException(
                nameof(drainModel),
                drainModel,
                "The BYOC Runtime drain model is unsupported.");
        }

        lock (_operationLock)
        {
            if (_operation is null)
            {
                ByocRuntimeDrainStart start = _workerManager.CloseAdmission();
                DrainOperation operation = new(
                    drainModel,
                    _timeProvider.GetUtcNow(),
                    _timeProvider.GetTimestamp(),
                    start);
                _operation = operation;

                if (drainModel is ByocDrainModel.Immediate)
                {
                    SelectAllRemainingLocked(operation);
                }
                else
                {
                    SelectNonHttpBatchLocked(operation, batchIndex: 0);
                    if (!operation.SelectionFinalized)
                    {
                        _ = RunPhasedScheduleAsync(operation);
                    }
                }

                Log.Accepted(_logger, drainModel, operation.AcceptedAtUtc);
                return operation.CreateReceipt();
            }

            if (_operation.ActiveModel == drainModel)
            {
                return _operation.CreateReceipt();
            }

            if (_operation.ActiveModel is ByocDrainModel.Immediate)
            {
                throw new ByocRuntimeDrainConflictException(
                    "The Runtime is already draining with the Immediate model.");
            }

            _operation.ActiveModel = ByocDrainModel.Immediate;
            _operation.ScheduleCancellation.Cancel();
            SelectAllRemainingLocked(_operation);
            Log.Escalated(_logger, _operation.AcceptedAtUtc);

            return _operation.CreateReceipt();
        }
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
        catch (Exception exception)
        {
            Log.ScheduleFailed(_logger, exception);
        }
    }

    private bool CanRunPhasedSelectionLocked(DrainOperation operation)
        => ReferenceEquals(_operation, operation) &&
            operation.ActiveModel is ByocDrainModel.Phased &&
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
        _workerManager.SelectWorkers(workerIds);
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
            ByocDrainModel activeModel,
            DateTimeOffset acceptedAtUtc,
            long acceptedTimestamp,
            ByocRuntimeDrainStart start)
        {
            ActiveModel = activeModel;
            AcceptedAtUtc = acceptedAtUtc;
            AcceptedTimestamp = acceptedTimestamp;
            Start = start;
            AllWorkerIds = [.. start.NonHttpWorkerIds, .. start.HttpWorkerIds];
        }

        public DateTimeOffset AcceptedAtUtc { get; }

        public long AcceptedTimestamp { get; }

        public ByocDrainModel ActiveModel { get; set; }

        public IReadOnlyList<string> AllWorkerIds { get; }

        public CancellationTokenSource ScheduleCancellation { get; } = new();

        public HashSet<string> SelectedWorkerIds { get; } = new(StringComparer.Ordinal);

        public bool SelectionFinalized { get; set; }

        public ByocRuntimeDrainStart Start { get; }

        public ByocRuntimeDrainReceipt CreateReceipt() => new(ActiveModel, AcceptedAtUtc);
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Information,
            "Accepted BYOC Runtime drain with {DrainModel} at {AcceptedAtUtc}.")]
        public static partial void Accepted(
            ILogger logger,
            ByocDrainModel drainModel,
            DateTimeOffset acceptedAtUtc);

        [LoggerMessage(1, LogLevel.Information,
            "Escalated BYOC Runtime drain to Immediate; original acceptance was {AcceptedAtUtc}.")]
        public static partial void Escalated(
            ILogger logger,
            DateTimeOffset acceptedAtUtc);

        [LoggerMessage(2, LogLevel.Error, "BYOC Runtime phased drain schedule failed.")]
        public static partial void ScheduleFailed(
            ILogger logger,
            Exception exception);
    }
}
