// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed class RuntimeStateManagerTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Current_SumsOnlyRegistryReadyHttpChannelsWithoutScriptHost()
    {
        await using ClientWorkerChannelTestHarness http = await CreateReadyChannelAsync("http", "http", "10");
        await using ClientWorkerChannelTestHarness pending = await CreateReadyChannelAsync("pending", "HTTP", "13");
        await using ClientWorkerChannelTestHarness background = await CreateReadyChannelAsync("background", "background");
        await using RuntimeHarness harness = new();
        harness.Publish([http.Channel, pending.Channel, background.Channel], [http.Channel, background.Channel]);
        await harness.Manager.StartAsync(CancellationToken.None);
        RuntimeState current = await WaitForStateAsync(harness.Manager, state => state.WorkerCount == 3);
        Assert.Equal(2, current.HttpWorkerCount);
        Assert.Equal(10, current.HttpCapacity);

        harness.Publish([http.Channel, pending.Channel, background.Channel], [http.Channel, pending.Channel, background.Channel]);
        RuntimeState ready = await WaitForStateAsync(harness.Manager, state => state.HttpCapacity == 23);
        Assert.Equal(3, ready.WorkerCount);
        Assert.Equal(2, ready.HttpWorkerCount);
    }

    [Fact]
    public async Task RootStoppingIsPermanentWhileCountsCanStillChange()
    {
        await using ClientWorkerChannelTestHarness first = await CreateReadyChannelAsync("first", "http", "10");
        await using ClientWorkerChannelTestHarness second = await CreateReadyChannelAsync("second", "http", "13");
        await using RuntimeHarness harness = new();
        harness.Publish([first.Channel], [first.Channel]);
        await harness.Manager.StartAsync(CancellationToken.None);
        await WaitForStateAsync(harness.Manager, state => state.HttpCapacity == 10);

        harness.RootStopping.Cancel();
        Assert.Equal(0, harness.Manager.Current.HttpCapacity);
        harness.Publish([first.Channel, second.Channel], [first.Channel, second.Channel]);
        RuntimeState state = await WaitForStateAsync(harness.Manager, state => state.WorkerCount == 2);

        Assert.Equal(0, state.HttpCapacity);
        Assert.Equal(2, state.HttpWorkerCount);
    }

    [Fact]
    public async Task GetChangeTask_SharesSignalAndIsolatesCallerCancellation()
    {
        await using ClientWorkerChannelTestHarness channel = await CreateReadyChannelAsync("http", "http", "10");
        await using RuntimeHarness harness = new();
        Task changed = harness.Manager.GetChangeTask();
        RuntimeState initial = harness.Manager.Current;
        Assert.Same(changed, harness.Manager.GetChangeTask());
        using CancellationTokenSource canceled = new();
        Task canceledWait = changed.WaitAsync(canceled.Token);
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledWait);
        Assert.False(changed.IsCompleted);
        harness.Publish([channel.Channel], []);
        await harness.Manager.StartAsync(CancellationToken.None);

        await changed.WaitAsync(TestTimeout);
        RuntimeState current = harness.Manager.Current;
        Assert.Equal(1, current.WorkerCount);
        Assert.Equal(0, current.HttpCapacity);
        Assert.NotSame(initial, current);
        Task nextChange = harness.Manager.GetChangeTask();
        Assert.NotSame(changed, nextChange);
        Assert.Same(nextChange, harness.Manager.GetChangeTask());
        Assert.False(nextChange.IsCompleted);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => nextChange.WaitAsync(canceled.Token));
        Assert.False(nextChange.IsCompleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetChangeTask_CapturedBeforeReadingCurrentDoesNotMissChanges(bool changeBeforeRead)
    {
        await using ClientWorkerChannelTestHarness channel = await CreateReadyChannelAsync("http", "http", "10");
        await using RuntimeHarness harness = new();
        harness.Publish([channel.Channel], [channel.Channel]);
        await harness.Manager.StartAsync(CancellationToken.None);
        await WaitForStateAsync(harness.Manager, state => state.HttpCapacity == 10);
        Task changed = harness.Manager.GetChangeTask();
        if (changeBeforeRead)
        {
            harness.RootStopping.Cancel();
        }

        RuntimeState sampled = harness.Manager.Current;
        if (!changeBeforeRead)
        {
            harness.RootStopping.Cancel();
        }

        await changed.WaitAsync(TestTimeout);

        Assert.Equal(changeBeforeRead ? 0 : 10, sampled.HttpCapacity);
        Assert.Equal(0, harness.Manager.Current.HttpCapacity);
        Assert.False(harness.Manager.GetChangeTask().IsCompleted);
    }

    [Fact]
    public async Task Current_DeduplicatesUnchangedValuesAndCreatesUtcTimestampsOnlyForChanges()
    {
        await using ClientWorkerChannelTestHarness channel = await CreateReadyChannelAsync("http", "http", "10");
        await using ClientWorkerChannelTestHarness second = await CreateReadyChannelAsync("second", "http", "13");
        await using RuntimeHarness harness = new();
        harness.UtcNow = new(2026, 10, 22, 12, 0, 0, TimeSpan.FromHours(2));
        harness.Publish([channel.Channel], [channel.Channel]);
        await harness.Manager.StartAsync(CancellationToken.None);
        RuntimeState available = await WaitForStateAsync(harness.Manager, state => state.HttpCapacity == 10);
        Assert.Equal(TimeSpan.Zero, available.CreatedTime.Offset);
        Assert.Equal(harness.UtcNow.ToUniversalTime(), available.CreatedTime);
        Task changedSignal = harness.Manager.GetChangeTask();
        harness.UtcNow = harness.UtcNow.AddMinutes(1);
        harness.Publish([channel.Channel], [channel.Channel]);
        await Task.Delay(50);
        Assert.Same(available, harness.Manager.Current);
        Assert.Same(changedSignal, harness.Manager.GetChangeTask());
        Assert.False(changedSignal.IsCompleted);

        harness.Publish([channel.Channel, second.Channel], [channel.Channel, second.Channel]);
        RuntimeState changed = await WaitForStateAsync(harness.Manager, state => state.HttpCapacity == 23);
        Assert.NotSame(available, changed);
        Assert.True(changedSignal.IsCompletedSuccessfully);
        Assert.Equal(harness.UtcNow.ToUniversalTime(), changed.CreatedTime);
        Assert.Equal(2, changed.WorkerCount);
    }

    [Fact]
    public async Task Current_InvalidationDuringSamplingDiscardsMixedInputsEvenWhenMembershipReturns()
    {
        await using ClientWorkerChannelTestHarness channel = await CreateReadyChannelAsync("http", "http", "10");
        await using RuntimeHarness harness = new();
        harness.OnInitializedRead = () =>
        {
            harness.Publish([channel.Channel], []);
            harness.Publish([], []);
            harness.Publish([channel.Channel], [channel.Channel]);
        };

        await harness.Manager.StartAsync(CancellationToken.None);
        RuntimeState current = await WaitForStateAsync(harness.Manager, state => state.HttpCapacity == 10);

        Assert.Equal(1, current.WorkerCount);
        Assert.DoesNotContain(harness.Snapshots, snapshot => snapshot.Capacity > 0 && snapshot.Workers == 0);
    }

    [Fact]
    public async Task Current_RootStopDuringSamplingCannotPublishStalePositiveCapacity()
    {
        await using ClientWorkerChannelTestHarness channel = await CreateReadyChannelAsync("http", "http", "10");
        await using RuntimeHarness harness = new();
        harness.Publish([channel.Channel], [channel.Channel]);
        harness.OnInitializedRead = () => harness.RootStopping.Cancel();

        await harness.Manager.StartAsync(CancellationToken.None);
        RuntimeState state = await WaitForStateAsync(harness.Manager, value => value.WorkerCount == 1);

        Assert.Equal(0, state.HttpCapacity);
        Assert.DoesNotContain(harness.Snapshots, snapshot => snapshot.Capacity > 0);
    }

    [Fact]
    public async Task Current_CompletedReadySnapshotDoesNotCauseRepeatedWakeups()
    {
        await using ClientWorkerChannelTestHarness channel = await CreateReadyChannelAsync("http", "http", "10");
        await using RuntimeHarness harness = new();
        harness.Publish([channel.Channel], [channel.Channel]);
        await harness.Manager.StartAsync(CancellationToken.None);
        await WaitForStateAsync(harness.Manager, state => state.HttpCapacity == 10);
        harness.Registry.Invocations.Clear();

        await Task.Delay(50);

        harness.Registry.Verify(registry => registry.GetInitializedChannels(), Times.Never);
        harness.Registry.Verify(registry => registry.GetReadyChannelsAsync(), Times.Never);
    }

    [Fact]
    public async Task Current_RecomputeFailureRecoversOnInputChangeWithoutStoppingRoot()
    {
        await using ClientWorkerChannelTestHarness channel = await CreateReadyChannelAsync("http", "http", "10");
        await using RuntimeHarness harness = new();
        harness.Publish([channel.Channel], [channel.Channel]);
        harness.OnInitializedRead = () => throw new InvalidOperationException("Transient sampling failure.");
        await harness.Manager.StartAsync(CancellationToken.None);
        await harness.RecomputeFailed.Task.WaitAsync(TestTimeout);
        Assert.Equal(0, harness.Manager.Current.HttpCapacity);

        harness.Publish([channel.Channel], [channel.Channel]);
        await WaitForStateAsync(harness.Manager, state => state.HttpCapacity == 10);

        harness.Lifetime.Verify(lifetime => lifetime.StopApplication(), Times.Never);
    }

    [Fact]
    public async Task Current_FaultedChangeSourceIsObservedAndWithdrawsCapacity()
    {
        await using RuntimeHarness harness = new();
        harness.Registry.Setup(registry => registry.GetChangeTask()).Returns(Task.FromException(new InvalidOperationException("Change source failed.")));

        await harness.Manager.StartAsync(CancellationToken.None);
        await harness.Manager.ExecuteTask!.WaitAsync(TestTimeout);

        Assert.True(harness.Manager.ExecuteTask.IsCompletedSuccessfully);
        Assert.Contains(902, harness.EventIds);
        Assert.Equal(0, harness.Manager.Current.HttpCapacity);
        harness.Lifetime.Verify(lifetime => lifetime.StopApplication(), Times.Never);
    }

    [Fact]
    public async Task RootStopping_WithdrawsEvenIfConfiguredClockFails()
    {
        await using ClientWorkerChannelTestHarness channel = await CreateReadyChannelAsync("http", "http", "10");
        await using RuntimeHarness harness = new();
        harness.Publish([channel.Channel], [channel.Channel]);
        await harness.Manager.StartAsync(CancellationToken.None);
        await WaitForStateAsync(harness.Manager, state => state.HttpCapacity == 10);
        harness.Clock.Setup(clock => clock.GetUtcNow()).Throws(new InvalidOperationException("Clock failed."));

        harness.RootStopping.Cancel();

        Assert.Equal(0, harness.Manager.Current.HttpCapacity);
        Assert.Equal(TimeSpan.Zero, harness.Manager.Current.CreatedTime.Offset);
        Assert.Contains(904, harness.EventIds);
    }

    private static async Task<ClientWorkerChannelTestHarness> CreateReadyChannelAsync(string id, string group, string? concurrency = null)
    {
        ClientWorkerChannelTestHarness harness = await ClientWorkerChannelTestHarness.CreateAsync(
            id, functionGroupName: group, maxHttpConcurrency: concurrency);
        harness.Channel.SetupFunctionInvocationBuffers([new FunctionMetadata { Name = "Function", Language = "external" }]);
        return harness;
    }

    private static async Task<RuntimeState> WaitForStateAsync(RuntimeStateManager manager, Func<RuntimeState, bool> predicate)
    {
        using CancellationTokenSource timeout = new(TestTimeout);
        while (true)
        {
            Task changed = manager.GetChangeTask();
            RuntimeState state = manager.Current;
            if (predicate(state))
            {
                return state;
            }

            await changed.WaitAsync(timeout.Token);
        }
    }

    private sealed class RuntimeHarness : IAsyncDisposable
    {
        private readonly Lock _stateLock = new();
        private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<IReadOnlyList<WorkerChannel>> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private IReadOnlyList<WorkerChannel> _initialized = [];
        private Action? _onInitializedRead;

        public RuntimeHarness()
        {
            Clock.Setup(clock => clock.GetUtcNow()).Returns(() => UtcNow);
            Lifetime.SetupGet(lifetime => lifetime.ApplicationStopping).Returns(RootStopping.Token);
            Logger.Setup(logger => logger.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            Logger.Setup(logger => logger.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
                .Callback(new InvocationAction(invocation =>
                {
                    EventId eventId = (EventId)invocation.Arguments[1];
                    EventIds.Enqueue(eventId.Id);
                    if (eventId.Id == 900)
                    {
                        var fields = (IEnumerable<KeyValuePair<string, object?>>)invocation.Arguments[2];
                        long capacity = (long)fields.Single(field => string.Equals(field.Key, "HttpCapacity", StringComparison.Ordinal)).Value!;
                        int workers = (int)fields.Single(field => string.Equals(field.Key, "WorkerCount", StringComparison.Ordinal)).Value!;
                        Snapshots.Enqueue((capacity, workers));
                    }

                    if (eventId.Id == 901)
                    {
                        RecomputeFailed.TrySetResult();
                    }
                }));
            Registry.Setup(registry => registry.GetChangeTask()).Returns(() =>
            {
                lock (_stateLock)
                {
                    return _changed.Task;
                }
            });
            Registry.Setup(registry => registry.GetInitializedChannels()).Returns(() =>
            {
                IReadOnlyList<WorkerChannel> snapshot;
                lock (_stateLock)
                {
                    snapshot = _initialized;
                }

                Interlocked.Exchange(ref _onInitializedRead, null)?.Invoke();
                return snapshot;
            });
            Registry.Setup(registry => registry.GetReadyChannelsAsync()).Returns(() =>
            {
                lock (_stateLock)
                {
                    return new(_ready.Task);
                }
            });
            Manager = new(Registry.Object, Lifetime.Object, Clock.Object, Logger.Object);
        }

        public Mock<TimeProvider> Clock { get; } = new() { CallBase = true };

        public ConcurrentQueue<int> EventIds { get; } = new();

        public Mock<IHostApplicationLifetime> Lifetime { get; } = new();

        public Mock<ILogger<RuntimeStateManager>> Logger { get; } = new();

        public RuntimeStateManager Manager { get; }

        public Action OnInitializedRead { set => Interlocked.Exchange(ref _onInitializedRead, value); }

        public TaskCompletionSource RecomputeFailed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Mock<IWorkerChannelRegistry> Registry { get; } = new(MockBehavior.Strict);

        public CancellationTokenSource RootStopping { get; } = new();

        public ConcurrentQueue<(long Capacity, int Workers)> Snapshots { get; } = new();

        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;

        public void Publish(IReadOnlyList<WorkerChannel> initialized, IReadOnlyList<WorkerChannel> ready)
        {
            lock (_stateLock)
            {
                _initialized = initialized;
                if (ready.Count > 0)
                {
                    _ready.TrySetResult(ready);
                    _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _ready.SetResult(ready);
                }
                else if (_ready.Task.IsCompleted)
                {
                    _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }

                TaskCompletionSource changed = _changed;
                _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
                changed.TrySetResult();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Manager.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
            Manager.Dispose();
            RootStopping.Dispose();
        }
    }
}
