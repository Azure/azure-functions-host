// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
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

    private readonly Lock _channelsLock = new();
    private readonly List<WorkerChannel> _channels = [];
    private readonly List<IAsyncDisposable> _workers = [];
    private readonly Mock<IWorkerChannelRegistry> _registry = new(MockBehavior.Strict);
    private readonly Mock<IScriptHostManager> _scriptHostManager = new(MockBehavior.Strict);
    private readonly Mock<IHostApplicationLifetime> _applicationLifetime = new(MockBehavior.Strict);
    private readonly Mock<TimeProvider> _timeProvider = new() { CallBase = true };
    private readonly CancellationTokenSource _applicationStopping = new();
    private long _channelSetVersion;
    private TaskCompletionSource _channelSetChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile ScriptHostState _scriptHostState = ScriptHostState.Starting;
    private long _nowUnixMilliseconds = StartTime.ToUnixTimeMilliseconds();

    public ComputeRuntimeStateManagerTests()
    {
        _registry.Setup(registry => registry.GetInitializedChannels())
            .Returns(() => GetChannels());
        _registry.SetupGet(registry => registry.ChannelSetVersion)
            .Returns(() => Volatile.Read(ref _channelSetVersion));
        _registry.Setup(registry => registry.WaitForChannelSetChangeAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns((long lastKnownVersion, CancellationToken cancellationToken) => WaitForChannelSetChangeAsync(lastKnownVersion, cancellationToken));
        _scriptHostManager.SetupGet(manager => manager.State)
            .Returns(() => _scriptHostState);
        _applicationLifetime.SetupGet(lifetime => lifetime.ApplicationStopping)
            .Returns(_applicationStopping.Token);
        _timeProvider.Setup(provider => provider.GetUtcNow())
            .Returns(() => DateTimeOffset.FromUnixTimeMilliseconds(Volatile.Read(ref _nowUnixMilliseconds)));
    }

    [Fact]
    public async Task Current_BeforeScriptHostIsRunning_ReportsZeroCounts()
    {
        await LinkWorkerAsync("worker", "http", ready: true);
        using ComputeRuntimeStateManager manager = await StartManagerAsync();
        ComputeRuntimeState initial = manager.Current;

        SetScriptHostState(ScriptHostState.Running);
        ComputeRuntimeState running = await manager.WaitForChangeAsync(initial.SnapshotVersion).WaitAsync(TestTimeout);

        Assert.Equal(new ComputeRuntimeState(StartTime.ToUnixTimeMilliseconds(), 0, 0), initial);
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
    public async Task Current_RecomputeFailure_LogsErrorAndRecoversOnNextPoll()
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

        // No channel-set change follows the failure, so only the next poll can recover.
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
    public async Task ApplicationStopping_PublishesZeroCountsAndSuppressesLaterPositiveCounts()
    {
        SetScriptHostState(ScriptHostState.Running);
        await LinkWorkerAsync("first", "http", ready: true);
        using ComputeRuntimeStateManager manager = await StartManagerAsync();
        ComputeRuntimeState running = await WaitForStateAsync(manager, linkedWorkerCount: 1, linkedHttpWorkerCount: 1);

        _applicationStopping.Cancel();
        ComputeRuntimeState stopping = manager.Current;
        Task<ComputeRuntimeState> laterChange = manager.WaitForChangeAsync(stopping.SnapshotVersion);
        await LinkWorkerAsync("second", "http", ready: true);

        // Give the manager several poll intervals to observe the new worker before it stops.
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        await manager.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.True(stopping.SnapshotVersion > running.SnapshotVersion);
        Assert.Equal(0, stopping.LinkedWorkerCount);
        Assert.Equal(0, stopping.LinkedHttpWorkerCount);
        Assert.False(laterChange.IsCompleted);
        Assert.Same(stopping, manager.Current);
    }

    [Fact]
    public async Task StopAsync_PublishesZeroCountsWithoutApplicationStopping()
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
    public async Task SnapshotVersion_IncreasesWhenClockStallsOrMovesBackward()
    {
        SetScriptHostState(ScriptHostState.Running);
        using ComputeRuntimeStateManager manager = await StartManagerAsync();
        long initialVersion = manager.Current.SnapshotVersion;

        RpcClientWorkerChannel first = await LinkWorkerAsync("first", "http", ready: true);
        ComputeRuntimeState stalled = await WaitForStateAsync(manager, linkedWorkerCount: 1, linkedHttpWorkerCount: 1);
        SetNow(StartTime.AddMinutes(-5));
        UnlinkWorker(first);
        ComputeRuntimeState backward = await WaitForStateAsync(manager, linkedWorkerCount: 0, linkedHttpWorkerCount: 0);
        SetNow(StartTime.AddMinutes(5));
        await LinkWorkerAsync("second", "durable", ready: true);
        ComputeRuntimeState forward = await WaitForStateAsync(manager, linkedWorkerCount: 1, linkedHttpWorkerCount: 0);

        Assert.Equal(initialVersion + 1, stalled.SnapshotVersion);
        Assert.Equal(stalled.SnapshotVersion + 1, backward.SnapshotVersion);
        Assert.Equal(StartTime.AddMinutes(5).ToUnixTimeMilliseconds(), forward.SnapshotVersion);
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
            TimeSpan.FromMilliseconds(10));
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

        SignalChannelSetChanged();

        return worker.Channel;
    }

    private void UnlinkWorker(WorkerChannel channel)
    {
        lock (_channelsLock)
        {
            _channels.Remove(channel);
        }

        SignalChannelSetChanged();
    }

    private void SetNow(DateTimeOffset now) => Volatile.Write(ref _nowUnixMilliseconds, now.ToUnixTimeMilliseconds());

    private void SetScriptHostState(ScriptHostState state) => _scriptHostState = state;

    private WorkerChannel[] GetChannels()
    {
        lock (_channelsLock)
        {
            return [.. _channels];
        }
    }

    private void SignalChannelSetChanged()
    {
        Interlocked.Increment(ref _channelSetVersion);
        Interlocked.Exchange(ref _channelSetChanged, new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
    }

    private async Task<long> WaitForChannelSetChangeAsync(long lastKnownVersion, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task changed = Volatile.Read(ref _channelSetChanged).Task;
            long version = Volatile.Read(ref _channelSetVersion);
            if (version > lastKnownVersion)
            {
                return version;
            }

            await changed.WaitAsync(cancellationToken);
        }
    }
}
