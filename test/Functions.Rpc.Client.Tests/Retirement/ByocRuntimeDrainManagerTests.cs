// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed class ByocRuntimeDrainManagerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PhasedDrain_UsesFixedScheduleOrEscalatesRemainingWorkers(
        bool escalate)
    {
        RecordingWorkerManager workerManager = new(
            ["non-http-1", "non-http-2", "non-http-3", "non-http-4", "non-http-5", "non-http-6"],
            ["http-1"]);
        TimeProvider timeProvider = escalate
            ? TimeProvider.System
            : new AcceleratedTimeProvider();
        ByocRuntimeDrainManager manager = new(
            workerManager,
            timeProvider,
            NullLogger<ByocRuntimeDrainManager>.Instance);

        ByocRuntimeDrainReceipt phased = manager.Accept(ByocDrainModel.Phased);
        ByocRuntimeDrainReceipt replay = manager.Accept(ByocDrainModel.Phased);
        ByocRuntimeDrainReceipt active = escalate
            ? manager.Accept(ByocDrainModel.Immediate)
            : phased;
        await workerManager.SelectionCompleted.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(phased, replay);
        Assert.Equal(phased.AcceptedAtUtc, active.AcceptedAtUtc);
        if (escalate)
        {
            Assert.Equal(ByocDrainModel.Immediate, active.DrainModel);
            Assert.Equal(
            [
                ["non-http-1", "non-http-2"],
                ["non-http-3", "non-http-4", "non-http-5", "non-http-6", "http-1"],
            ],
            workerManager.Selections);
            Assert.Throws<ByocRuntimeDrainConflictException>(
                () => manager.Accept(ByocDrainModel.Phased));
        }
        else
        {
            Assert.Equal(ByocDrainModel.Phased, active.DrainModel);
            Assert.Equal(
            [
                ["non-http-1", "non-http-2"],
                ["non-http-3"],
                ["non-http-4"],
                ["non-http-5"],
                ["http-1"],
                ["non-http-6"],
            ],
            workerManager.Selections);
        }
    }

    private sealed class RecordingWorkerManager : IByocRuntimeDrainWorkerManager
    {
        private readonly string[] _allWorkerIds;
        private readonly HashSet<string> _selectedWorkerIds = new(StringComparer.Ordinal);
        private readonly Lock _selectionLock = new();
        private readonly List<string[]> _selections = [];
        private readonly TaskCompletionSource _selectionCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly ByocRuntimeDrainStart _start;

        public RecordingWorkerManager(
            IReadOnlyList<string> nonHttpWorkerIds,
            IReadOnlyList<string> httpWorkerIds)
        {
            _start = new(nonHttpWorkerIds, httpWorkerIds);
            _allWorkerIds = [.. nonHttpWorkerIds, .. httpWorkerIds];
        }

        public Task SelectionCompleted => _selectionCompleted.Task;

        public IReadOnlyList<string[]> Selections
        {
            get
            {
                lock (_selectionLock)
                {
                    return [.. _selections];
                }
            }
        }

        public ByocRuntimeDrainStart CloseAdmission() => _start;

        public void SelectWorkers(IReadOnlyList<string> workerIds)
        {
            lock (_selectionLock)
            {
                string[] selection = [.. workerIds];
                _selections.Add(selection);
                _selectedWorkerIds.UnionWith(selection);
                if (_selectedWorkerIds.Count == _allWorkerIds.Length)
                {
                    _selectionCompleted.TrySetResult();
                }
            }
        }
    }

    private sealed class AcceleratedTimeProvider : TimeProvider
    {
        private const long Scale = 1000;
        private readonly long _originTimestamp = Stopwatch.GetTimestamp();

        public override long TimestampFrequency => Stopwatch.Frequency;

        public override long GetTimestamp()
            => _originTimestamp +
                ((Stopwatch.GetTimestamp() - _originTimestamp) * Scale);

        public override ITimer CreateTimer(
            TimerCallback callback,
            object state,
            TimeSpan dueTime,
            TimeSpan period)
            => TimeProvider.System.CreateTimer(
                callback,
                state,
                Accelerate(dueTime),
                Accelerate(period));

        private static TimeSpan Accelerate(TimeSpan value)
            => value == Timeout.InfiniteTimeSpan
                ? value
                : TimeSpan.FromTicks(Math.Max(1, value.Ticks / Scale));
    }
}
