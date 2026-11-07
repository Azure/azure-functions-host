// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed class RuntimeDrainManagerTests
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
        await using RuntimeDrainManager manager = new(
            workerManager,
            Mock.Of<IHostApplicationLifetime>(),
            timeProvider,
            NullLogger<RuntimeDrainManager>.Instance);

        RuntimeDrainReceipt phased = manager.Accept(RuntimeDrainMode.Phased);
        RuntimeDrainReceipt replay = manager.Accept(RuntimeDrainMode.Phased);
        RuntimeDrainReceipt active = escalate
            ? manager.Accept(RuntimeDrainMode.Immediate)
            : phased;
        await workerManager.SelectionCompleted.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(phased, replay);
        Assert.Equal(phased.AcceptedAtUtc, active.AcceptedAtUtc);
        if (escalate)
        {
            Assert.Equal(RuntimeDrainMode.Immediate, active.DrainMode);
            Assert.Equal(
            [
                ["non-http-1", "non-http-2"],
                ["non-http-3", "non-http-4", "non-http-5", "non-http-6", "http-1"],
            ],
            workerManager.Selections);
            Assert.Throws<RuntimeDrainConflictException>(
                () => manager.Accept(RuntimeDrainMode.Phased));
        }
        else
        {
            Assert.Equal(RuntimeDrainMode.Phased, active.DrainMode);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PhasedDrain_HostShutdownOrDisposalCancelsRemainingSelections(bool dispose)
    {
        FakeTimeProvider time = new();
        using CancellationTokenSource stopping = new();
        Mock<IHostApplicationLifetime> lifetime = new();
        lifetime.SetupGet(value => value.ApplicationStopping).Returns(stopping.Token);
        Mock<IRuntimeDrainWorkerManager> workers = new(MockBehavior.Strict);
        workers.Setup(value => value.CloseAdmission()).Returns(new RuntimeDrainStart(
            ["non-http-1", "non-http-2"], ["http-1"]));
        workers.Setup(value => value.SelectWorkers(It.IsAny<IReadOnlyList<string>>()));
        Mock<ILogger<RuntimeDrainManager>> logger = new();
        logger.Setup(value => value.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        await using RuntimeDrainManager manager = new(workers.Object, lifetime.Object, time, logger.Object);
        manager.Accept(RuntimeDrainMode.Phased);

        time.Advance(TimeSpan.FromSeconds(9));
        if (dispose)
        {
            await manager.DisposeAsync();
            await manager.DisposeAsync();
            Assert.Throws<ObjectDisposedException>(() => manager.Accept(RuntimeDrainMode.Immediate));
        }
        else
        {
            stopping.Cancel();
        }

        await manager.ScheduleCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        time.Advance(TimeSpan.FromHours(1));

        workers.Verify(value => value.CloseAdmission(), Times.Once);
        workers.Verify(value => value.SelectWorkers(It.IsAny<IReadOnlyList<string>>()), Times.Once);
        logger.Verify(value => value.Log(
            LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Never);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PhasedDrain_SelectionFailureOnlySuppressesExpectedShutdownDisposal(bool shutdown, bool registryDisposed)
    {
        FakeTimeProvider time = new();
        using CancellationTokenSource stopping = new();
        Mock<IHostApplicationLifetime> lifetime = new();
        lifetime.SetupGet(value => value.ApplicationStopping).Returns(stopping.Token);
        Mock<IRuntimeDrainWorkerManager> workers = new(MockBehavior.Strict);
        workers.Setup(value => value.CloseAdmission()).Returns(new RuntimeDrainStart(
            ["non-http-1", "non-http-2"], ["http-1"]));
        Exception failure = registryDisposed
            ? new ObjectDisposedException(nameof(WorkerChannelRegistry))
            : new InvalidOperationException("Selection failed.");
        int selectionCount = 0;
        workers.Setup(value => value.SelectWorkers(It.IsAny<IReadOnlyList<string>>()))
            .Callback(() =>
            {
                if (Interlocked.Increment(ref selectionCount) == 2)
                {
                    if (shutdown)
                    {
                        stopping.Cancel();
                    }

                    throw failure;
                }
            });
        Mock<ILogger<RuntimeDrainManager>> logger = new();
        logger.Setup(value => value.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        await using RuntimeDrainManager manager = new(workers.Object, lifetime.Object, time, logger.Object);
        manager.Accept(RuntimeDrainMode.Phased);

        time.Advance(TimeSpan.FromSeconds(10));
        await manager.ScheduleCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        time.Advance(TimeSpan.FromHours(1));

        workers.Verify(value => value.SelectWorkers(It.IsAny<IReadOnlyList<string>>()), Times.Exactly(2));
        logger.Verify(value => value.Log(
            LogLevel.Error, It.Is<EventId>(eventId => eventId.Id == 2), It.IsAny<It.IsAnyType>(),
            failure, It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            shutdown && registryDisposed ? Times.Never() : Times.Once());
    }

    [Theory]
    [InlineData(RuntimeDrainMode.Phased, 1, false)]
    [InlineData(RuntimeDrainMode.Immediate, 1, false)]
    [InlineData(RuntimeDrainMode.Phased, 2, false)]
    [InlineData(RuntimeDrainMode.Phased, 2, true)]
    public async Task SelectionFailure_ReplayRethrowsWithoutReselectingWorkers(
        RuntimeDrainMode initialMode,
        int failOnSelection,
        bool escalate)
    {
        FakeTimeProvider time = new();
        InvalidOperationException failure = new("Selection failed after partially draining a batch.");
        int selectionCount = 0;
        HashSet<string> selected = new(StringComparer.Ordinal);
        Mock<IRuntimeDrainWorkerManager> workers = new(MockBehavior.Strict);
        workers.Setup(value => value.CloseAdmission()).Returns(new RuntimeDrainStart(
            ["non-http-1", "non-http-2", "non-http-3", "non-http-4", "non-http-5"], ["http-1"]));
        workers.Setup(value => value.SelectWorkers(It.IsAny<IReadOnlyList<string>>()))
            .Callback<IReadOnlyList<string>>(workerIds =>
            {
                if (++selectionCount == failOnSelection)
                {
                    selected.Add(workerIds[0]);
                    throw failure;
                }

                selected.UnionWith(workerIds);
            });
        await using RuntimeDrainManager manager = new(
            workers.Object, Mock.Of<IHostApplicationLifetime>(), time, NullLogger<RuntimeDrainManager>.Instance);

        if (failOnSelection == 1)
        {
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => manager.Accept(initialMode)));
        }
        else
        {
            manager.Accept(initialMode);
            if (escalate)
            {
                Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => manager.Accept(RuntimeDrainMode.Immediate)));
            }
            else
            {
                time.Advance(TimeSpan.FromSeconds(10));
            }
        }

        await manager.ScheduleCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.WhenAll(Enumerable.Range(0, 16).Select(index => Task.Run(() =>
        {
            RuntimeDrainMode mode = index % 2 == 0 ? RuntimeDrainMode.Phased : RuntimeDrainMode.Immediate;
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => manager.Accept(mode)));
        }))).WaitAsync(TimeSpan.FromSeconds(5));
        time.Advance(TimeSpan.FromHours(2));

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => manager.Accept(initialMode)));
        Assert.InRange(selected.Count, 1, 5);
        workers.Verify(value => value.CloseAdmission(), Times.Once);
        workers.Verify(value => value.SelectWorkers(It.IsAny<IReadOnlyList<string>>()), Times.Exactly(failOnSelection));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PhasedDrain_SchedulingFailureIsPreservedOnReplay(int failOnTimer)
    {
        FakeTimeProvider time = new();
        InvalidOperationException failure = new("Scheduling failed.");
        int timerCount = 0;
        Mock<TimeProvider> timeProvider = new();
        timeProvider.Setup(value => value.GetUtcNow()).Returns(time.GetUtcNow);
        timeProvider.Setup(value => value.GetTimestamp()).Returns(time.GetTimestamp);
        timeProvider.SetupGet(value => value.TimestampFrequency).Returns(time.TimestampFrequency);
        timeProvider.Setup(value => value.CreateTimer(
                It.IsAny<TimerCallback>(), It.IsAny<object>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
            .Returns<TimerCallback, object, TimeSpan, TimeSpan>((callback, state, dueTime, period) =>
            {
                if (++timerCount == failOnTimer)
                {
                    throw failure;
                }

                return time.CreateTimer(callback, state, dueTime, period);
            });
        Mock<IRuntimeDrainWorkerManager> workers = new(MockBehavior.Strict);
        workers.Setup(value => value.CloseAdmission()).Returns(new RuntimeDrainStart(
            ["non-http-1", "non-http-2", "non-http-3"], ["http-1"]));
        workers.Setup(value => value.SelectWorkers(It.IsAny<IReadOnlyList<string>>()));
        await using RuntimeDrainManager manager = new(
            workers.Object, Mock.Of<IHostApplicationLifetime>(), timeProvider.Object, NullLogger<RuntimeDrainManager>.Instance);

        if (failOnTimer == 1)
        {
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => manager.Accept(RuntimeDrainMode.Phased)));
        }
        else
        {
            manager.Accept(RuntimeDrainMode.Phased);
            time.Advance(TimeSpan.FromSeconds(10));
        }

        await manager.ScheduleCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => manager.Accept(RuntimeDrainMode.Phased)));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => manager.Accept(RuntimeDrainMode.Immediate)));
        time.Advance(TimeSpan.FromHours(2));

        workers.Verify(value => value.CloseAdmission(), Times.Once);
        workers.Verify(value => value.SelectWorkers(It.IsAny<IReadOnlyList<string>>()), Times.Exactly(failOnTimer));
        Assert.Equal(failOnTimer, timerCount);
    }

    [Fact]
    public async Task PhasedDrain_SelectionFailureIsVisibleBeforeScheduleFinishes()
    {
        FakeTimeProvider time = new();
        InvalidOperationException failure = new("Selection failed.");
        Mock<IRuntimeDrainWorkerManager> workers = new(MockBehavior.Strict);
        workers.Setup(value => value.CloseAdmission()).Returns(new RuntimeDrainStart(
            ["non-http-1", "non-http-2"], ["http-1"]));
        workers.SetupSequence(value => value.SelectWorkers(It.IsAny<IReadOnlyList<string>>()))
            .Pass()
            .Throws(failure);
        Mock<ILogger<RuntimeDrainManager>> logger = new();
        logger.Setup(value => value.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        TaskCompletionSource logging = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using ManualResetEventSlim finishLogging = new();
        logger.Setup(value => value.Log(
                LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                failure, It.IsAny<Func<It.IsAnyType, Exception, string>>()))
            .Callback(() =>
            {
                logging.TrySetResult();
                Assert.True(finishLogging.Wait(TimeSpan.FromSeconds(5)));
            });
        await using RuntimeDrainManager manager = new(
            workers.Object, Mock.Of<IHostApplicationLifetime>(), time, logger.Object);
        // The blocking logger must not occupy xUnit's synchronization context.
        await Task.Run(() => manager.Accept(RuntimeDrainMode.Phased));

        Task advance = Task.Run(() => time.Advance(TimeSpan.FromSeconds(10)));
        try
        {
            await logging.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(manager.ScheduleCompletion.IsCompleted);
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => manager.Accept(RuntimeDrainMode.Phased)));
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => manager.Accept(RuntimeDrainMode.Immediate)));
        }
        finally
        {
            finishLogging.Set();
        }

        await advance.WaitAsync(TimeSpan.FromSeconds(5));
        await manager.ScheduleCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        workers.Verify(value => value.SelectWorkers(It.IsAny<IReadOnlyList<string>>()), Times.Exactly(2));
    }

    [Fact]
    public async Task DisposeAsync_DoesNotHoldOperationLockWhileCancellationCallbacksRun()
    {
        TaskCompletionSource callbackEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using ManualResetEventSlim finishCallback = new();
        Mock<ITimer> timer = new();
        timer.Setup(value => value.Dispose()).Callback(() =>
        {
            callbackEntered.TrySetResult();
            Assert.True(finishCallback.Wait(TimeSpan.FromSeconds(5)));
        });
        Mock<TimeProvider> time = new() { CallBase = true };
        time.Setup(value => value.CreateTimer(
                It.IsAny<TimerCallback>(), It.IsAny<object>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
            .Returns(timer.Object);
        Mock<IRuntimeDrainWorkerManager> workers = new(MockBehavior.Strict);
        workers.Setup(value => value.CloseAdmission()).Returns(new RuntimeDrainStart(
            ["non-http-1", "non-http-2"], ["http-1"]));
        workers.Setup(value => value.SelectWorkers(It.IsAny<IReadOnlyList<string>>()));
        await using RuntimeDrainManager manager = new(
            workers.Object, Mock.Of<IHostApplicationLifetime>(), time.Object, NullLogger<RuntimeDrainManager>.Instance);
        manager.Accept(RuntimeDrainMode.Phased);

        Task disposal = manager.DisposeAsync().AsTask();
        try
        {
            await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Run(() =>
            {
                _ = manager.ScheduleCompletion;
                Assert.Throws<ObjectDisposedException>(() => manager.Accept(RuntimeDrainMode.Immediate));
            }).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(disposal.IsCompleted);
        }
        finally
        {
            finishCallback.Set();
        }

        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        workers.Verify(value => value.SelectWorkers(It.IsAny<IReadOnlyList<string>>()), Times.Once);
    }

    private sealed class RecordingWorkerManager : IRuntimeDrainWorkerManager
    {
        private readonly string[] _allWorkerIds;
        private readonly HashSet<string> _selectedWorkerIds = new(StringComparer.Ordinal);
        private readonly Lock _selectionLock = new();
        private readonly List<string[]> _selections = [];
        private readonly TaskCompletionSource _selectionCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly RuntimeDrainStart _start;

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

        public RuntimeDrainStart CloseAdmission() => _start;

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
