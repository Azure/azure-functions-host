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

    internal Task WaitForRetirementAsync(CancellationToken cancellationToken = default)
    {
        Task retirement;
        lock (_operationLock)
        {
            retirement = _operation?.Retirement.Task
                ?? throw new InvalidOperationException("The Runtime drain has not been accepted.");
        }

        return retirement.WaitAsync(cancellationToken);
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

                TrackSelectionLocked(operation, _workerManager.SelectWorkers(operation.Start.HttpWorkerIds));
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
            lock (_operationLock)
            {
                operation.Retirement.TrySetException(exception);
            }

            Log.ScheduleFailed(_logger, exception);
        }
    }

    private bool CanRunPhasedSelectionLocked(DrainOperation operation)
        => ReferenceEquals(_operation, operation) &&
            operation.ActiveModel is ByocDrainModel.Phased &&
            !operation.SelectionFinalized;

    private void SelectNonHttpBatchLocked(DrainOperation operation, int batchIndex)
    {
        IReadOnlyList<string> workerIds = operation.Start.NonHttpWorkerIds
            .Skip(batchIndex * operation.NonHttpBatchSize)
            .Take(operation.NonHttpBatchSize)
            .ToArray();
        TrackSelectionLocked(operation, _workerManager.SelectWorkers(workerIds));
    }

    private void SelectAllRemainingLocked(DrainOperation operation)
    {
        string[] remainingWorkerIds = operation.AllWorkerIds
            .Where(workerId => !operation.SelectedWorkerIds.Contains(workerId))
            .ToArray();
        TrackSelectionLocked(operation, _workerManager.SelectWorkers(remainingWorkerIds));
    }

    private void TrackSelectionLocked(
        DrainOperation operation,
        ByocRuntimeDrainSelection selection)
    {
        foreach (string workerId in selection.WorkerIds)
        {
            operation.SelectedWorkerIds.Add(workerId);
        }

        foreach (Task<bool> drainTask in selection.DrainTasks)
        {
            operation.DrainTasks.Add(drainTask);
            _ = drainTask.ContinueWith(
                static (_, state) =>
                {
                    (ByocRuntimeDrainManager manager, DrainOperation drainOperation) =
                        ((ByocRuntimeDrainManager, DrainOperation))state!;
                    manager.OnDrainCompleted(drainOperation);
                },
                (this, operation),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        if (operation.SelectedWorkerIds.Count == operation.AllWorkerIds.Count)
        {
            operation.SelectionFinalized = true;
            operation.ScheduleCancellation.Cancel();
        }

        TryCompleteRetirementLocked(operation);
    }

    private void OnDrainCompleted(DrainOperation operation)
    {
        lock (_operationLock)
        {
            TryCompleteRetirementLocked(operation);
        }
    }

    private void TryCompleteRetirementLocked(DrainOperation operation)
    {
        if (!ReferenceEquals(_operation, operation) ||
            !operation.SelectionFinalized ||
            operation.Retirement.Task.IsCompleted ||
            operation.DrainTasks.Any(static task => !task.IsCompleted))
        {
            return;
        }

        Task<bool> failedTask = operation.DrainTasks.FirstOrDefault(
            static task => !task.IsCompletedSuccessfully || !task.Result);
        if (failedTask is not null)
        {
            Exception failure = failedTask.Exception?.GetBaseException()
                ?? new InvalidOperationException("A selected Worker did not complete its Runtime drain.");
            operation.Retirement.TrySetException(failure);
            return;
        }

        operation.Retirement.TrySetResult();
        Log.Retired(_logger, operation.AcceptedAtUtc);
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
            NonHttpBatchSize = start.NonHttpWorkerIds.Count == 0
                ? 0
                : Math.Max(1, (start.NonHttpWorkerIds.Count + 4) / 5);
        }

        public DateTimeOffset AcceptedAtUtc { get; }

        public long AcceptedTimestamp { get; }

        public ByocDrainModel ActiveModel { get; set; }

        public IReadOnlyList<string> AllWorkerIds { get; }

        public List<Task<bool>> DrainTasks { get; } = [];

        public int NonHttpBatchSize { get; }

        public TaskCompletionSource Retirement { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

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

        [LoggerMessage(2, LogLevel.Information,
            "BYOC Runtime retirement completed without an empty-reuse grace period; accepted at {AcceptedAtUtc}.")]
        public static partial void Retired(
            ILogger logger,
            DateTimeOffset acceptedAtUtc);

        [LoggerMessage(3, LogLevel.Error, "BYOC Runtime phased drain schedule failed.")]
        public static partial void ScheduleFailed(
            ILogger logger,
            Exception exception);
    }
}
