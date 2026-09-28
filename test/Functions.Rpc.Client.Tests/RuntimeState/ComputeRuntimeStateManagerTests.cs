// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed class ComputeRuntimeStateManagerTests : IAsyncDisposable
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);
    private static readonly DateTimeOffset StartTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan StartTimestamp = TimeSpan.FromHours(1);

    private readonly Lock _channelsLock = new();
    private readonly List<WorkerChannel> _channels = [];
    private readonly List<IAsyncDisposable> _workers = [];
    private readonly Mock<IWorkerChannelRegistry> _registry = new(MockBehavior.Strict);
    private readonly Mock<IScriptHostManager> _scriptHostManager = new(MockBehavior.Strict);
    private readonly Mock<IHostApplicationLifetime> _applicationLifetime = new(MockBehavior.Strict);
    private readonly Mock<TimeProvider> _timeProvider = new() { CallBase = true };
    private readonly CancellationTokenSource _applicationStopping = new();
    private readonly InitializedChannelsSignal _initializedChannelsSignal = new();
    private readonly ScriptHostStateSignal _scriptHostStateSignal = new(ScriptHostState.Starting);
    private long _wallClockUnixMilliseconds = StartTime.ToUnixTimeMilliseconds();
    private long _timestampTicks = StartTimestamp.Ticks;

    public ComputeRuntimeStateManagerTests()
    {
        _registry.Setup(registry => registry.GetInitializedChannels())
            .Returns(() => GetChannels());
        _registry.SetupGet(registry => registry.InitializedChannelsVersion)
            .Returns(() => _initializedChannelsSignal.Version);
        _registry.Setup(registry => registry.WaitForInitializedChannelsChangeAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns((long lastKnownVersion, CancellationToken cancellationToken) =>
                _initializedChannelsSignal.WaitForChangeAsync(lastKnownVersion, cancellationToken));
        _scriptHostManager.SetupGet(manager => manager.State)
            .Returns(() => _scriptHostStateSignal.State);
        _scriptHostManager.Setup(manager => manager.WaitForStateChangeAsync(It.IsAny<ScriptHostState>(), It.IsAny<CancellationToken>()))
            .Returns((ScriptHostState lastKnownState, CancellationToken cancellationToken) =>
                _scriptHostStateSignal.WaitForChangeAsync(lastKnownState, cancellationToken));
        _applicationLifetime.SetupGet(lifetime => lifetime.ApplicationStopping)
            .Returns(_applicationStopping.Token);
        _timeProvider.Setup(provider => provider.GetUtcNow())
            .Returns(() => DateTimeOffset.FromUnixTimeMilliseconds(Volatile.Read(ref _wallClockUnixMilliseconds)));
        _timeProvider.SetupGet(provider => provider.TimestampFrequency)
            .Returns(TimeSpan.TicksPerSecond);
        _timeProvider.Setup(provider => provider.GetTimestamp())
            .Returns(() => Volatile.Read(ref _timestampTicks));
    }

    [Fact]
    public async Task Current_BeforeScriptHostIsRunning_ReportsZeroCounts()
    {
        await LinkWorkerAsync("worker", "http", ready: true);
        using ComputeRuntimeStateManager manager = await StartManagerAsync();
        ComputeRuntimeState initial = manager.Current;

        SetScriptHostState(ScriptHostState.Running);
        ComputeRuntimeState running = await manager.WaitForChangeAsync(initial.SnapshotVersion).WaitAsync(TestTimeout);

        Assert.Equal(new ComputeRuntimeState((long)StartTimestamp.TotalMilliseconds, 0, 0), initial);
        Assert.Equal(1, running.LinkedWorkerCount);
        Assert.Equal(1, running.LinkedHttpWorkerCount);
    }

    [Fact]
    public async Task Current_CountsOnlyReadyHttpGroupWorkersAsHttpWorkers()
    {
        SetScriptHostState(ScriptHostState.Running);
        await LinkWorkerAsync("http-ready", "http", ready: true);
        await LinkWorkerAsync("http-uppercase-ready", "HTTP", ready: true);
        await LinkWorkerAsync("http-not-ready", "http", ready: false);
        await LinkWorkerAsync("durable-ready", "durable", ready: true);
        await LinkWorkerAsync("no-group-ready", functionGroupName: null, ready: true);
        using ComputeRuntimeStateManager manager = await StartManagerAsync();

        ComputeRuntimeState state = await WaitForStateAsync(manager, linkedWorkerCount: 5, linkedHttpWorkerCount: 2);

        Assert.Equal(5, state.LinkedWorkerCount);
        Assert.Equal(2, state.LinkedHttpWorkerCount);
    }

    [Fact]
    public async Task Current_TracksHttpWorkerReadinessAndUnlink()
    {
        SetScriptHostState(ScriptHostState.Running);
        using ComputeRuntimeStateManager manager = await StartManagerAsync();
        RpcClientWorkerChannel channel = await LinkWorkerAsync("worker", "http", ready: false);
        await WaitForStateAsync(manager, linkedWorkerCount: 1, linkedHttpWorkerCount: 0);

        channel.SetupFunctionInvocationBuffers([]);
        await WaitForStateAsync(manager, linkedWorkerCount: 1, linkedHttpWorkerCount: 1);

        UnlinkWorker(channel);
        await WaitForStateAsync(manager, linkedWorkerCount: 0, linkedHttpWorkerCount: 0);

        // Every change woke the manager directly, so it never needed a timer.
        _timeProvider.Verify(
            provider => provider.CreateTimer(It.IsAny<TimerCallback>(), It.IsAny<object>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()),
            Times.Never());
    }

    [Fact]
    public async Task Current_WaitsForScriptHostStateChangesFromTheLastReadState()
    {
        // Each wait completes only when the test completes it, like a wait that has not yet observed a change.
        ConcurrentDictionary<ScriptHostState, TaskCompletionSource<ScriptHostState>> stateWaits = new();
        _scriptHostManager.Setup(manager => manager.WaitForStateChangeAsync(It.IsAny<ScriptHostState>(), It.IsAny<CancellationToken>()))
            .Returns((ScriptHostState lastKnownState, CancellationToken cancellationToken) => stateWaits
                .GetOrAdd(lastKnownState, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).Task
                .WaitAsync(cancellationToken));
        SetScriptHostState(ScriptHostState.Running);
        await LinkWorkerAsync("first", "http", ready: true);
        using ComputeRuntimeStateManager manager = await StartManagerAsync();
        await WaitForStateAsync(manager, linkedWorkerCount: 1, linkedHttpWorkerCount: 1);

        // The pending Running wait never completes, so the registry change is the only wake.
        SetScriptHostState(ScriptHostState.Default);
        await LinkWorkerAsync("second", "http", ready: true);
        await WaitForStateAsync(manager, linkedWorkerCount: 0, linkedHttpWorkerCount: 0);

        // The Running wait still misses the return to Running, so only a wait on the Default state wakes the manager.
        SetScriptHostState(ScriptHostState.Running);
        Assert.True(stateWaits.TryGetValue(ScriptHostState.Default, out TaskCompletionSource<ScriptHostState> defaultStateWait));
        defaultStateWait.SetResult(ScriptHostState.Running);
        await WaitForStateAsync(manager, linkedWorkerCount: 2, linkedHttpWorkerCount: 2);
    }

    [Theory]
    [InlineData(ScriptHostState.Default)]
    [InlineData(ScriptHostState.Initialized)]
    [InlineData(ScriptHostState.Error)]
    [InlineData(ScriptHostState.Offline)]
    public async Task Current_WhenScriptHostLeavesRunning_ReportsZeroCountsUntilItIsRunningAgain(ScriptHostState notRunningState)
    {
        SetScriptHostState(ScriptHostState.Running);
        await LinkWorkerAsync("worker", "http", ready: true);
        using ComputeRuntimeStateManager manager = await StartManagerAsync();
        ComputeRuntimeState running = await WaitForStateAsync(manager, linkedWorkerCount: 1, linkedHttpWorkerCount: 1);

        SetScriptHostState(notRunningState);
        ComputeRuntimeState notRunning = await WaitForStateAsync(manager, linkedWorkerCount: 0, linkedHttpWorkerCount: 0);
        SetScriptHostState(ScriptHostState.Running);
        ComputeRuntimeState runningAgain = await WaitForStateAsync(manager, linkedWorkerCount: 1, linkedHttpWorkerCount: 1);

        Assert.True(notRunning.SnapshotVersion > running.SnapshotVersion);
        Assert.True(runningAgain.SnapshotVersion > notRunning.SnapshotVersion);
    }

    [Fact]
    public async Task Current_RecomputeFailure_LogsErrorAndRecoversOnRetry()
    {
        Mock<ILogger> logger = new();
        using ILoggerFactory loggerFactory = CreateLoggerFactoryWithHostFilters(logger);
        int snapshotCount = 0;
        _registry.Setup(registry => registry.GetInitializedChannels())
            .Returns(() => Interlocked.Increment(ref snapshotCount) == 1
                ? throw new InvalidOperationException("Channel snapshot failed.")
                : GetChannels());
        SetScriptHostState(ScriptHostState.Running);
        await LinkWorkerAsync("worker", "http", ready: true);
        using ComputeRuntimeStateManager manager = await StartManagerAsync(loggerFactory);

        // No input changes after the failure, so only the retry can recover.
        await WaitForStateAsync(manager, linkedWorkerCount: 1, linkedHttpWorkerCount: 1);

        logger.Verify(value => value.Log(
            LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.Is<Exception>(exception => exception is InvalidOperationException),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once());
    }

    [Fact]
    public async Task WaitForChangeAsync_ReturnsImmediatelyForOlderVersionAndSupportsCancellation()
    {
        using ComputeRuntimeStateManager manager = await StartManagerAsync();
        ComputeRuntimeState current = manager.Current;
        using CancellationTokenSource cancellationSource = new();

        Task<ComputeRuntimeState> staleWait = manager.WaitForChangeAsync(current.SnapshotVersion - 1);
        Task<ComputeRuntimeState> canceledWait = manager.WaitForChangeAsync(current.SnapshotVersion, cancellationSource.Token);
        cancellationSource.Cancel();

        Assert.True(staleWait.IsCompletedSuccessfully);
        Assert.Equal(current, await staleWait);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledWait.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task ApplicationStopping_RecordsZeroCountsAndSuppressesLaterPositiveCounts()
    {
        TaskCompletionSource secondWorkerCounted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _registry.Setup(registry => registry.GetInitializedChannels())
            .Returns(() =>
            {
                WorkerChannel[] channels = GetChannels();
                if (channels.Length == 2)
                {
                    secondWorkerCounted.TrySetResult();
                }

                return channels;
            });
        SetScriptHostState(ScriptHostState.Running);
        await LinkWorkerAsync("first", "http", ready: true);
        using ComputeRuntimeStateManager manager = await StartManagerAsync();
        ComputeRuntimeState running = await WaitForStateAsync(manager, linkedWorkerCount: 1, linkedHttpWorkerCount: 1);

        _applicationStopping.Cancel();
        ComputeRuntimeState stopping = manager.Current;
        Task<ComputeRuntimeState> laterChange = manager.WaitForChangeAsync(stopping.SnapshotVersion);
        await LinkWorkerAsync("second", "http", ready: true);

        // Wait until a recompute sees the new worker; StopAsync then waits for that recompute to finish.
        await secondWorkerCounted.Task.WaitAsync(TestTimeout);
        await manager.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.True(stopping.SnapshotVersion > running.SnapshotVersion);
        Assert.Equal(0, stopping.LinkedWorkerCount);
        Assert.Equal(0, stopping.LinkedHttpWorkerCount);
        Assert.False(laterChange.IsCompleted);
        Assert.Same(stopping, manager.Current);
    }

    [Fact]
    public async Task StopAsync_RecordsZeroCountsWithoutApplicationStopping()
    {
        SetScriptHostState(ScriptHostState.Running);
        await LinkWorkerAsync("worker", "http", ready: true);
        using ComputeRuntimeStateManager manager = await StartManagerAsync();
        await WaitForStateAsync(manager, linkedWorkerCount: 1, linkedHttpWorkerCount: 1);

        await manager.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Equal(0, manager.Current.LinkedWorkerCount);
        Assert.Equal(0, manager.Current.LinkedHttpWorkerCount);
    }

    [Fact]
    public async Task ExecuteAsync_UnexpectedFailure_LogsErrorAndStillRecordsZeroCountsOnStop()
    {
        Mock<ILogger> logger = new();
        using ILoggerFactory loggerFactory = CreateLoggerFactoryWithHostFilters(logger);
        _scriptHostManager.Setup(manager => manager.WaitForStateChangeAsync(It.IsAny<ScriptHostState>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ScriptHost state wait failed."));
        SetScriptHostState(ScriptHostState.Running);
        await LinkWorkerAsync("worker", "http", ready: true);
        using ComputeRuntimeStateManager manager = await StartManagerAsync(loggerFactory);

        // A faulted BackgroundService task stops the Host, so the failure must complete the task normally.
        await manager.ExecuteTask.WaitAsync(TestTimeout);
        ComputeRuntimeState afterFailure = manager.Current;
        await manager.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Equal(1, afterFailure.LinkedWorkerCount);
        Assert.Equal(1, afterFailure.LinkedHttpWorkerCount);
        Assert.Equal(0, manager.Current.LinkedWorkerCount);
        Assert.Equal(0, manager.Current.LinkedHttpWorkerCount);
        logger.Verify(value => value.Log(
            LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.Is<Exception>(exception => exception is InvalidOperationException),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once());
    }

    [Fact]
    public async Task SnapshotVersion_IncreasesWhenMonotonicClockStallsAndIgnoresWallClock()
    {
        SetScriptHostState(ScriptHostState.Running);
        using ComputeRuntimeStateManager manager = await StartManagerAsync();
        long initialVersion = manager.Current.SnapshotVersion;

        RpcClientWorkerChannel first = await LinkWorkerAsync("first", "http", ready: true);
        ComputeRuntimeState stalled = await WaitForStateAsync(manager, linkedWorkerCount: 1, linkedHttpWorkerCount: 1);
        SetWallClock(StartTime.AddMinutes(5));
        UnlinkWorker(first);
        ComputeRuntimeState wallClockMoved = await WaitForStateAsync(manager, linkedWorkerCount: 0, linkedHttpWorkerCount: 0);
        AdvanceMonotonicClock(TimeSpan.FromMinutes(5));
        await LinkWorkerAsync("second", "durable", ready: true);
        ComputeRuntimeState advanced = await WaitForStateAsync(manager, linkedWorkerCount: 1, linkedHttpWorkerCount: 0);

        Assert.Equal(initialVersion + 1, stalled.SnapshotVersion);
        Assert.Equal(stalled.SnapshotVersion + 1, wallClockMoved.SnapshotVersion);
        Assert.Equal((long)(StartTimestamp + TimeSpan.FromMinutes(5)).TotalMilliseconds, advanced.SnapshotVersion);
    }

    [Fact]
    public async Task SnapshotVersion_AfterHostProcessRestartExceedsPreviousVersionsWhenWallClockMovesBackward()
    {
        SetScriptHostState(ScriptHostState.Running);
        await LinkWorkerAsync("worker", "http", ready: true);
        using ComputeRuntimeStateManager previousManager = await StartManagerAsync();
        await WaitForStateAsync(previousManager, linkedWorkerCount: 1, linkedHttpWorkerCount: 1);
        await previousManager.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
        ComputeRuntimeState previous = previousManager.Current;

        // The system monotonic clock keeps counting across Host process restarts, even when the wall clock moves backward.
        AdvanceMonotonicClock(TimeSpan.FromSeconds(1));
        SetWallClock(StartTime.AddMinutes(-5));
        using ComputeRuntimeStateManager manager = await StartManagerAsync();

        Assert.Equal(0, previous.LinkedWorkerCount);
        Assert.True(manager.Current.SnapshotVersion > previous.SnapshotVersion);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (IAsyncDisposable worker in _workers)
        {
            await worker.DisposeAsync();
        }

        _applicationStopping.Dispose();
    }

    private async Task<ComputeRuntimeStateManager> StartManagerAsync(ILoggerFactory loggerFactory = null)
    {
        ComputeRuntimeStateManager manager = new(
            _registry.Object,
            _scriptHostManager.Object,
            _applicationLifetime.Object,
            _timeProvider.Object,
            loggerFactory ?? NullLoggerFactory.Instance,
            recomputeRetryDelay: TimeSpan.FromMilliseconds(10));
        await manager.StartAsync(CancellationToken.None);

        return manager;
    }

    private static ILoggerFactory CreateLoggerFactoryWithHostFilters(Mock<ILogger> logger)
    {
        logger.Setup(value => value.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        Mock<ILoggerProvider> provider = new();
        provider.Setup(value => value.CreateLogger(It.IsAny<string>())).Returns(logger.Object);

        // Apply the Host's root log filters, which drop categories without a system prefix.
        return LoggerFactory.Create(builder => builder.AddDefaultWebJobsFilters().AddProvider(provider.Object));
    }

    private static async Task<ComputeRuntimeState> WaitForStateAsync(
        ComputeRuntimeStateManager manager, int linkedWorkerCount, int linkedHttpWorkerCount)
    {
        using CancellationTokenSource timeoutSource = new(TestTimeout);
        ComputeRuntimeState state = manager.Current;
        while (state.LinkedWorkerCount != linkedWorkerCount || state.LinkedHttpWorkerCount != linkedHttpWorkerCount)
        {
            state = await manager.WaitForChangeAsync(state.SnapshotVersion, timeoutSource.Token);
        }

        return state;
    }

    private async Task<RpcClientWorkerChannel> LinkWorkerAsync(string workerId, string functionGroupName, bool ready)
    {
        ClientWorkerChannelTestHarness worker =
            await ClientWorkerChannelTestHarness.CreateAsync(workerId, functionGroupName: functionGroupName);
        _workers.Add(worker);
        if (ready)
        {
            worker.Channel.SetupFunctionInvocationBuffers([]);
        }

        lock (_channelsLock)
        {
            _channels.Add(worker.Channel);
        }

        _initializedChannelsSignal.Signal();

        return worker.Channel;
    }

    private void UnlinkWorker(WorkerChannel channel)
    {
        lock (_channelsLock)
        {
            _channels.Remove(channel);
        }

        _initializedChannelsSignal.Signal();
    }

    private void SetWallClock(DateTimeOffset now) => Volatile.Write(ref _wallClockUnixMilliseconds, now.ToUnixTimeMilliseconds());

    private void AdvanceMonotonicClock(TimeSpan elapsed) => Interlocked.Add(ref _timestampTicks, elapsed.Ticks);

    private void SetScriptHostState(ScriptHostState state) => _scriptHostStateSignal.Set(state);

    private WorkerChannel[] GetChannels()
    {
        lock (_channelsLock)
        {
            return [.. _channels];
        }
    }
}
