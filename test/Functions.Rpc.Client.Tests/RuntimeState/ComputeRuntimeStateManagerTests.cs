// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
    private readonly Mock<IServiceProvider> _scriptServices = new(MockBehavior.Strict);
    private readonly Mock<IRpcClientFunctionInvocationDispatcher> _dispatcher = new(MockBehavior.Strict);
    private readonly Mock<IHostApplicationLifetime> _applicationLifetime = new(MockBehavior.Strict);
    private readonly Mock<TimeProvider> _timeProvider = new() { CallBase = true };
    private readonly CancellationTokenSource _applicationStopping = new();
    private readonly InitializedChannelsSignal _initializedChannelsSignal = new();
    private readonly ScriptHostStateSignal _scriptHostStateSignal = new(ScriptHostState.Starting);
    private long _utcTicks = StartTime.UtcTicks;

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
        _scriptHostManager.SetupGet(manager => manager.StateVersion).Returns(() => _scriptHostStateSignal.Version);
        _scriptHostManager.SetupGet(manager => manager.Services).Returns(_scriptServices.Object);
        _scriptServices.Setup(provider => provider.GetService(typeof(IRpcClientFunctionInvocationDispatcher))).Returns(_dispatcher.Object);
        _dispatcher.SetupGet(dispatcher => dispatcher.State).Returns(FunctionInvocationDispatcherState.Initialized);
        _dispatcher.Setup(dispatcher => dispatcher.GetChannelSetupTask(It.IsAny<WorkerChannel>())).Returns(Task.CompletedTask);
        _scriptHostManager.Setup(manager => manager.WaitForStateChangeAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns((long lastKnownVersion, CancellationToken cancellationToken) =>
                _scriptHostStateSignal.WaitForChangeAsync(lastKnownVersion, cancellationToken));
        _applicationLifetime.SetupGet(lifetime => lifetime.ApplicationStopping)
            .Returns(_applicationStopping.Token);
        _timeProvider.Setup(provider => provider.GetUtcNow())
            .Returns(() => new DateTimeOffset(Volatile.Read(ref _utcTicks), TimeSpan.Zero));
    }

    [Fact]
    public async Task Current_BeforeScriptHostIsRunning_ReportsZeroCapacity()
    {
        await LinkWorkerAsync("worker", "http", ready: true);
        using ComputeRuntimeStateManager manager = await StartManagerAsync();
        ComputeRuntimeState initial = await WaitForStateAsync(manager, httpCapacity: 0, workerCount: 1, httpWorkerCount: 1);

        SetScriptHostState(ScriptHostState.Running);
        ComputeRuntimeState running = await WaitForStateAsync(manager, httpCapacity: 16, workerCount: 1, httpWorkerCount: 1);

        Assert.Equal(new ComputeRuntimeState(StartTime, 0, 1, 1), initial);
        Assert.NotSame(initial, running);
        Assert.Equal(16, running.HttpCapacity);
    }

    [Fact]
    public async Task Current_SumsDefaultConcurrencyOfOnlyReadyHttpGroupWorkers()
    {
        SetScriptHostState(ScriptHostState.Running);
        await LinkWorkerAsync("http-ready", "http", ready: true);
        await LinkWorkerAsync("http-uppercase-ready", "HTTP", ready: true);
        await LinkWorkerAsync("http-not-ready", "http", ready: false);
        await LinkWorkerAsync("durable-ready", "durable", ready: true);
        await LinkWorkerAsync("no-group-ready", functionGroupName: null, ready: true);
        using ComputeRuntimeStateManager manager = await StartManagerAsync();

        ComputeRuntimeState state = await WaitForStateAsync(manager, httpCapacity: 32);

        Assert.Equal(32, state.HttpCapacity);
        Assert.Equal(5, state.WorkerCount);
        Assert.Equal(3, state.HttpWorkerCount);
    }

    [Fact]
    public async Task Current_CountsNonHttpAndUnreadyWorkers_AndNotifiesCountOnlyChanges()
    {
        using ComputeRuntimeStateManager manager = await StartManagerAsync();
        ComputeRuntimeState initial = manager.Current;
        Assert.Equal(0, initial.WorkerCount);
        Assert.Equal(0, initial.HttpWorkerCount);

        RpcClientWorkerChannel unknown = await LinkWorkerAsync("unknown", null, ready: false);
        ComputeRuntimeState unknownLinked = await WaitForStateAsync(manager, 0, workerCount: 1, httpWorkerCount: 0);
        RpcClientWorkerChannel http = await LinkWorkerAsync("http", "HTTP", ready: false);
        ComputeRuntimeState httpLinked = await WaitForStateAsync(manager, 0, workerCount: 2, httpWorkerCount: 1);
        UnlinkWorker(unknown);
        ComputeRuntimeState unknownUnlinked = await WaitForStateAsync(manager, 0, workerCount: 1, httpWorkerCount: 1);
        UnlinkWorker(http);
        ComputeRuntimeState empty = await WaitForStateAsync(manager, 0, workerCount: 0, httpWorkerCount: 0);

        Assert.NotSame(initial, unknownLinked);
        Assert.NotSame(unknownLinked, httpLinked);
        Assert.NotSame(httpLinked, unknownUnlinked);
        Assert.NotSame(unknownUnlinked, empty);
    }

    [Fact]
    public async Task Current_CountOnlyChangesWithRunningHost_PreserveHttpCapacity()
    {
        SetScriptHostState(ScriptHostState.Running);
        await LinkWorkerAsync("http", "http", ready: true);
        using ComputeRuntimeStateManager manager = await StartManagerAsync();
        ComputeRuntimeState initial = await WaitForStateAsync(manager, 16, workerCount: 1, httpWorkerCount: 1);

        RpcClientWorkerChannel durable = await LinkWorkerAsync("durable", "durable", ready: true);
        ComputeRuntimeState linked = await WaitForStateAsync(manager, 16, workerCount: 2, httpWorkerCount: 1);
        UnlinkWorker(durable);
        ComputeRuntimeState unlinked = await WaitForStateAsync(manager, 16, workerCount: 1, httpWorkerCount: 1);

        Assert.NotSame(initial, linked);
        Assert.NotSame(linked, unlinked);
    }

    [Fact]
    public async Task Current_UnchangedCountsAndCapacity_DoNotCreateNewSnapshot()
    {
        Mock<ILogger<ComputeRuntimeStateManager>> logger = CreateLogger();
        SetScriptHostState(ScriptHostState.Running);
        await LinkWorkerAsync("http", "http", ready: true);
        using ComputeRuntimeStateManager manager = await StartManagerAsync(logger.Object);
        ComputeRuntimeState initial = await WaitForStateAsync(manager, 16, workerCount: 1, httpWorkerCount: 1);
        SetUtcTime(StartTime.AddMinutes(1));
        Assert.Same(initial, manager.Current);
        TaskCompletionSource recomputed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _registry.Setup(registry => registry.GetInitializedChannels()).Returns(() =>
        {
            recomputed.TrySetResult();
            return GetChannels();
        });

        _initializedChannelsSignal.Signal();
        await recomputed.Task.WaitAsync(TestTimeout);
        await manager.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Equal(StartTime, initial.CreatedTime);
        logger.Verify(value => value.Log(
            LogLevel.Information, It.Is<EventId>(eventId => eventId.Id == 900), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once());
        Assert.Equal(1, manager.Current.WorkerCount);
        Assert.Equal(1, manager.Current.HttpWorkerCount);
    }

    [Fact]
    public async Task Current_ExcludesWorkerWhoseDispatcherSetupFailedAfterBuffersInitialized()
    {
        Mock<IMetricsLogger> metrics = new();
        metrics.Setup(value => value.BeginEvent(MetricEventNames.FunctionLoadRequestResponse, It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new InvalidOperationException("Injected failure after buffer initialization."));
        RpcClientWorkerChannel healthy = await LinkWorkerAsync("healthy", "http", ready: false);
        RpcClientWorkerChannel failed = await LinkWorkerAsync("failed", "http", ready: false, metricsLogger: metrics.Object);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([new FunctionMetadata { Name = "TestFunction", Language = "external" }]);
        SetScriptHostState(ScriptHostState.Running);

        Assert.True(healthy.IsChannelReadyForInvocations());
        Assert.True(failed.IsChannelReadyForInvocations());
        using ComputeRuntimeStateManager manager = await StartManagerAsync();
        ComputeRuntimeState state = manager.Current;
        if (state.HttpCapacity == 0)
        {
            state = await manager.WaitForChangeAsync(state).WaitAsync(TestTimeout);
        }

        Assert.Equal(16, state.HttpCapacity);
        Assert.Equal(2, state.WorkerCount);
        Assert.Equal(2, state.HttpWorkerCount);
        metrics.Verify(value => value.BeginEvent(MetricEventNames.FunctionLoadRequestResponse, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        await dispatcher.ShutdownAsync().WaitAsync(TestTimeout);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Current_ObservesChannelBeforeDispatcherSetup_AndWakesOnOutcome(bool setupFails)
    {
        Mock<ILogger<ComputeRuntimeStateManager>> logger = CreateLogger();
        TaskCompletionSource<long> delayedRegistryChange = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _registry.Setup(registry => registry.WaitForInitializedChannelsChangeAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns((long _, CancellationToken token) => delayedRegistryChange.Task.WaitAsync(token));
        await LinkWorkerAsync("healthy", "http", ready: false);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([new FunctionMetadata { Name = "TestFunction", Language = "external" }]);
        Mock<IMetricsLogger> metrics = new();
        if (setupFails)
        {
            metrics.Setup(value => value.BeginEvent(MetricEventNames.FunctionLoadRequestResponse, It.IsAny<string>(), It.IsAny<string>()))
                .Throws(new InvalidOperationException("Injected failure after buffer initialization."));
        }

        RpcClientWorkerChannel pending = await LinkWorkerAsync("pending", "http", ready: true, metricsLogger: metrics.Object);
        SetScriptHostState(ScriptHostState.Running);
        using ComputeRuntimeStateManager manager = await StartManagerAsync(logger.Object);
        ComputeRuntimeState beforeSetup = manager.Current;
        if (beforeSetup.HttpCapacity == 0)
        {
            beforeSetup = await manager.WaitForChangeAsync(beforeSetup).WaitAsync(TestTimeout);
        }

        Assert.Equal(16, beforeSetup.HttpCapacity);
        Assert.True(pending.IsChannelReadyForInvocations());
        Task setup = dispatcher.GetChannelSetupTask(pending);
        Assert.False(setup.IsCompleted);
        TaskCompletionSource recomputed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _registry.Setup(registry => registry.GetInitializedChannels()).Returns(() =>
        {
            recomputed.TrySetResult();
            return GetChannels();
        });

        // Only setup completion can wake capacity; the registry watcher remains blocked.
        if (setupFails)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.SetupChannelAsync(pending));
        }
        else
        {
            Task completedSetup = dispatcher.SetupChannelAsync(pending);
            Assert.Same(setup, completedSetup);
            await completedSetup.WaitAsync(TestTimeout);
        }

        await recomputed.Task.WaitAsync(TestTimeout);
        if (setupFails)
        {
            await manager.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
            Assert.NotSame(beforeSetup, manager.Current);
            Assert.Equal(0, manager.Current.HttpCapacity);
            logger.Verify(value => value.Log(
                LogLevel.Information, It.Is<EventId>(eventId => eventId.Id == 900), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once());
        }
        else
        {
            await WaitForStateAsync(manager, httpCapacity: 32);
        }

        metrics.Verify(value => value.BeginEvent(MetricEventNames.FunctionLoadRequestResponse, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        _timeProvider.Verify(
            provider => provider.CreateTimer(It.IsAny<TimerCallback>(), It.IsAny<object>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()),
            Times.Never());
        await dispatcher.ShutdownAsync().WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task Current_AfterScriptHostRestart_UsesOnlyCurrentDispatcherSetup()
    {
        Mock<IMetricsLogger> metrics = new();
        RpcClientWorkerChannel worker = await LinkWorkerAsync("worker", "http", ready: false, metricsLogger: metrics.Object);
        await LinkWorkerAsync("healthy", "http", ready: false);
        FunctionMetadata function = new() { Name = "TestFunction", Language = "external" };
        using RpcClientFunctionInvocationDispatcher firstDispatcher = CreateDispatcher();
        await firstDispatcher.InitializeAsync([function]);
        SetScriptHostState(ScriptHostState.Running);
        using ComputeRuntimeStateManager manager = await StartManagerAsync();
        await WaitForStateAsync(manager, httpCapacity: 32);

        SetScriptHostState(ScriptHostState.Default);
        await WaitForStateAsync(manager, httpCapacity: 0);
        await firstDispatcher.ShutdownAsync().WaitAsync(TestTimeout);
        metrics.Setup(value => value.BeginEvent(MetricEventNames.FunctionLoadRequestResponse, It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new InvalidOperationException("Setup failed in the new ScriptHost."));
        using RpcClientFunctionInvocationDispatcher nextDispatcher = CreateDispatcher();
        await nextDispatcher.InitializeAsync([function]);
        Assert.True(firstDispatcher.GetChannelSetupTask(worker).IsCompletedSuccessfully);
        Assert.True(nextDispatcher.GetChannelSetupTask(worker).IsFaulted);
        Assert.True(worker.IsChannelReadyForInvocations());
        SetScriptHostState(ScriptHostState.Running);

        ComputeRuntimeState state = await WaitForStateAsync(manager, httpCapacity: 16);

        Assert.Equal(16, state.HttpCapacity);
        await nextDispatcher.ShutdownAsync().WaitAsync(TestTimeout);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Current_WithdrawsCapacityWhenScriptHostServicesAreUnavailable(bool disposed)
    {
        Mock<ILogger<ComputeRuntimeStateManager>> logger = CreateLogger();
        await LinkWorkerAsync("worker", "http", ready: true);
        SetScriptHostState(ScriptHostState.Running);
        using ComputeRuntimeStateManager manager = await StartManagerAsync(logger.Object);
        await WaitForStateAsync(manager, httpCapacity: 16);

        if (disposed)
        {
            _scriptServices.Setup(provider => provider.GetService(typeof(IRpcClientFunctionInvocationDispatcher)))
                .Throws(new ObjectDisposedException("ScriptHost"));
        }
        else
        {
            _scriptHostManager.SetupGet(host => host.Services).Returns((IServiceProvider)null);
        }

        _initializedChannelsSignal.Signal();

        await WaitForStateAsync(manager, httpCapacity: 0);
        await manager.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
        logger.Verify(value => value.Log(
            LogLevel.Debug, It.Is<EventId>(eventId => eventId.Id == 903), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), disposed ? Times.Once() : Times.Never());
    }

    [Theory]
    [InlineData(FunctionInvocationDispatcherState.Disposing)]
    [InlineData(FunctionInvocationDispatcherState.Disposed)]
    public async Task Current_WithdrawsCapacityWhenCurrentDispatcherStops(FunctionInvocationDispatcherState state)
    {
        await LinkWorkerAsync("worker", "http", ready: true);
        SetScriptHostState(ScriptHostState.Running);
        using ComputeRuntimeStateManager manager = await StartManagerAsync();
        await WaitForStateAsync(manager, httpCapacity: 16);

        _dispatcher.SetupGet(dispatcher => dispatcher.State).Returns(state);
        _initializedChannelsSignal.Signal();

        await WaitForStateAsync(manager, httpCapacity: 0);
    }

    [Fact]
    public async Task Current_TracksHttpWorkerReadinessAndUnlink()
    {
        SetScriptHostState(ScriptHostState.Running);
        RpcClientWorkerChannel pending = await LinkWorkerAsync("pending", "http", ready: false);
        RpcClientWorkerChannel ready = await LinkWorkerAsync("ready", "http", ready: true);
        using ComputeRuntimeStateManager manager = await StartManagerAsync();
        await WaitForStateAsync(manager, httpCapacity: 16);

        pending.SetupFunctionInvocationBuffers([]);
        await WaitForStateAsync(manager, httpCapacity: 32);

        UnlinkWorker(pending);
        await WaitForStateAsync(manager, httpCapacity: 16);
        UnlinkWorker(ready);
        await WaitForStateAsync(manager, httpCapacity: 0);

        // Every change woke the manager directly, so it never needed a timer.
        _timeProvider.Verify(
            provider => provider.CreateTimer(It.IsAny<TimerCallback>(), It.IsAny<object>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()),
            Times.Never());
    }

    [Fact]
    public async Task Current_RestartBeforeHostWaitBegins_RecomputesWithNewDispatcher()
    {
        TaskCompletionSource beginWait = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _scriptHostManager.Setup(manager => manager.WaitForStateChangeAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(async (long lastKnownVersion, CancellationToken token) =>
            {
                await beginWait.Task.WaitAsync(token);
                return await _scriptHostStateSignal.WaitForChangeAsync(lastKnownVersion, token);
            });
        SetScriptHostState(ScriptHostState.Running);
        await LinkWorkerAsync("worker", "http", ready: true);
        using ComputeRuntimeStateManager manager = await StartManagerAsync();
        await WaitForStateAsync(manager, httpCapacity: 16);

        SetScriptHostState(ScriptHostState.Default);
        Mock<IRpcClientFunctionInvocationDispatcher> nextDispatcher = new(MockBehavior.Strict);
        nextDispatcher.SetupGet(dispatcher => dispatcher.State).Returns(FunctionInvocationDispatcherState.Initialized);
        nextDispatcher.Setup(dispatcher => dispatcher.GetChannelSetupTask(It.IsAny<WorkerChannel>()))
            .Returns(Task.FromException(new InvalidOperationException("New dispatcher setup failed.")));
        _scriptServices.Setup(provider => provider.GetService(typeof(IRpcClientFunctionInvocationDispatcher))).Returns(nextDispatcher.Object);
        SetScriptHostState(ScriptHostState.Running);
        beginWait.SetResult();

        await WaitForStateAsync(manager, httpCapacity: 0);
    }

    [Fact]
    public async Task Current_ActiveHostReplacementWithoutStateChange_RecomputesCapacity()
    {
        SetScriptHostState(ScriptHostState.Running);
        await LinkWorkerAsync("worker", "http", ready: true);
        using ComputeRuntimeStateManager manager = await StartManagerAsync();
        await WaitForStateAsync(manager, httpCapacity: 16);
        Mock<IRpcClientFunctionInvocationDispatcher> nextDispatcher = new(MockBehavior.Strict);
        nextDispatcher.SetupGet(dispatcher => dispatcher.State).Returns(FunctionInvocationDispatcherState.Initialized);
        nextDispatcher.Setup(dispatcher => dispatcher.GetChannelSetupTask(It.IsAny<WorkerChannel>()))
            .Returns(Task.FromException(new InvalidOperationException("Replacement dispatcher setup failed.")));
        _scriptServices.Setup(provider => provider.GetService(typeof(IRpcClientFunctionInvocationDispatcher))).Returns(nextDispatcher.Object);

        _scriptHostStateSignal.SignalHostChange();

        await WaitForStateAsync(manager, httpCapacity: 0);
        Assert.Equal(ScriptHostState.Running, _scriptHostStateSignal.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Current_LifecycleChangeDuringRecompute_DoesNotPublishStaleCapacity(bool restart)
    {
        Mock<ILogger<ComputeRuntimeStateManager>> logger = CreateLogger();
        SetScriptHostState(ScriptHostState.Running);
        RpcClientWorkerChannel worker = await LinkWorkerAsync("worker", "http", ready: true);
        using ComputeRuntimeStateManager manager = await StartManagerAsync(logger.Object);
        await WaitForStateAsync(manager, httpCapacity: 16);
        TaskCompletionSource recomputing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using ManualResetEventSlim release = new();
        _dispatcher.Setup(dispatcher => dispatcher.GetChannelSetupTask(worker)).Returns(() =>
        {
            recomputing.TrySetResult();
            if (!release.Wait(TestTimeout))
            {
                throw new TimeoutException("Recompute was not released.");
            }

            return Task.CompletedTask;
        });

        await LinkWorkerAsync("later", "http", ready: true);
        try
        {
            await recomputing.Task.WaitAsync(TestTimeout);
            SetScriptHostState(ScriptHostState.Error);
            if (restart)
            {
                Mock<IRpcClientFunctionInvocationDispatcher> nextDispatcher = new(MockBehavior.Strict);
                nextDispatcher.SetupGet(dispatcher => dispatcher.State).Returns(FunctionInvocationDispatcherState.Initialized);
                nextDispatcher.Setup(dispatcher => dispatcher.GetChannelSetupTask(It.IsAny<WorkerChannel>()))
                    .Returns(Task.FromException(new InvalidOperationException("Replacement dispatcher setup failed.")));
                _scriptServices.Setup(provider => provider.GetService(typeof(IRpcClientFunctionInvocationDispatcher))).Returns(nextDispatcher.Object);
                SetScriptHostState(ScriptHostState.Running);
            }
        }
        finally
        {
            release.Set();
        }

        await WaitForStateAsync(manager, httpCapacity: 0);
        await manager.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
        logger.Verify(value => value.Log(
            LogLevel.Information, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state.ToString().Contains("HTTP capacity 32.", StringComparison.Ordinal)),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Never());
    }

    [Fact]
    public async Task ApplicationStopping_DuringRecompute_DoesNotRestorePositiveCapacity()
    {
        SetScriptHostState(ScriptHostState.Running);
        RpcClientWorkerChannel worker = await LinkWorkerAsync("worker", "http", ready: true);
        using ComputeRuntimeStateManager manager = await StartManagerAsync();
        await WaitForStateAsync(manager, httpCapacity: 16);
        TaskCompletionSource recomputing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using ManualResetEventSlim release = new();
        _dispatcher.Setup(dispatcher => dispatcher.GetChannelSetupTask(worker)).Returns(() =>
        {
            recomputing.TrySetResult();
            if (!release.Wait(TestTimeout))
            {
                throw new TimeoutException("Recompute was not released.");
            }

            return Task.CompletedTask;
        });
        await LinkWorkerAsync("later", "http", ready: true);
        ComputeRuntimeState stopping;
        try
        {
            await recomputing.Task.WaitAsync(TestTimeout);
            _applicationStopping.Cancel();
            stopping = manager.Current;
            Assert.Equal(0, stopping.HttpCapacity);
        }
        finally
        {
            release.Set();
        }

        await manager.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Equal(0, manager.Current.HttpCapacity);
        Assert.Equal(2, manager.Current.WorkerCount);
        Assert.Equal(2, manager.Current.HttpWorkerCount);
        Assert.NotSame(stopping, manager.Current);
    }

    [Fact]
    public async Task Current_RegistryChangeReusesPendingHostWait()
    {
        SetScriptHostState(ScriptHostState.Running);
        await LinkWorkerAsync("first", "http", ready: true);
        using ComputeRuntimeStateManager manager = await StartManagerAsync();
        await WaitForStateAsync(manager, httpCapacity: 16);

        await LinkWorkerAsync("second", "http", ready: true);
        await WaitForStateAsync(manager, httpCapacity: 32);

        _scriptHostManager.Verify(host => host.WaitForStateChangeAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(ScriptHostState.Default)]
    [InlineData(ScriptHostState.Initialized)]
    [InlineData(ScriptHostState.Error)]
    [InlineData(ScriptHostState.Offline)]
    public async Task Current_WhenScriptHostLeavesRunning_ReportsZeroCapacityUntilItIsRunningAgain(ScriptHostState notRunningState)
    {
        SetScriptHostState(ScriptHostState.Running);
        await LinkWorkerAsync("worker", "http", ready: true);
        using ComputeRuntimeStateManager manager = await StartManagerAsync();
        ComputeRuntimeState running = await WaitForStateAsync(manager, httpCapacity: 16);

        SetScriptHostState(notRunningState);
        ComputeRuntimeState notRunning = await WaitForStateAsync(manager, httpCapacity: 0);
        SetScriptHostState(ScriptHostState.Running);
        ComputeRuntimeState runningAgain = await WaitForStateAsync(manager, httpCapacity: 16);

        Assert.NotSame(running, notRunning);
        Assert.NotSame(notRunning, runningAgain);
        Assert.Equal(1, notRunning.WorkerCount);
        Assert.Equal(1, notRunning.HttpWorkerCount);
    }

    [Fact]
    public async Task Current_RecomputeFailure_LogsErrorAndRecoversOnRetry()
    {
        Mock<ILogger<ComputeRuntimeStateManager>> logger = CreateLogger();
        int snapshotCount = 0;
        _registry.Setup(registry => registry.GetInitializedChannels())
            .Returns(() => Interlocked.Increment(ref snapshotCount) == 1
                ? throw new InvalidOperationException("Channel snapshot failed.")
                : GetChannels());
        TaskCompletionSource<(TimerCallback Callback, object State, TimeSpan DueTime, TimeSpan Period)> retryTimer =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        _timeProvider.Setup(provider => provider.CreateTimer(
                It.IsAny<TimerCallback>(), It.IsAny<object>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
            .Returns((TimerCallback callback, object state, TimeSpan dueTime, TimeSpan period) =>
            {
                retryTimer.SetResult((callback, state, dueTime, period));
                return Mock.Of<ITimer>();
            });
        SetScriptHostState(ScriptHostState.Running);
        await LinkWorkerAsync("worker", "http", ready: true);
        using ComputeRuntimeStateManager manager = await StartManagerAsync(logger.Object);

        var retry = await retryTimer.Task.WaitAsync(TestTimeout);
        Assert.Equal(TimeSpan.FromSeconds(1), retry.DueTime);
        Assert.Equal(Timeout.InfiniteTimeSpan, retry.Period);
        Assert.Equal(0, manager.Current.HttpCapacity);

        // No input changes after the failure, so only the retry can recover.
        retry.Callback(retry.State);
        await WaitForStateAsync(manager, httpCapacity: 16);

        logger.Verify(value => value.Log(
            LogLevel.Error, It.Is<EventId>(eventId => eventId.Id == 901), It.IsAny<It.IsAnyType>(), It.Is<Exception>(exception => exception is InvalidOperationException),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once());
    }

    [Fact]
    public async Task WaitForChangeAsync_ReturnsImmediatelyForDifferentInstanceAndSupportsCancellation()
    {
        using ComputeRuntimeStateManager manager = await StartManagerAsync();
        ComputeRuntimeState current = manager.Current;
        using CancellationTokenSource cancellationSource = new();

        ComputeRuntimeState equivalent = current with { };
        Assert.Equal(current, equivalent);
        Task<ComputeRuntimeState> staleWait = manager.WaitForChangeAsync(equivalent);
        Task<ComputeRuntimeState> canceledWait = manager.WaitForChangeAsync(current, cancellationSource.Token);
        cancellationSource.Cancel();

        Assert.True(staleWait.IsCompletedSuccessfully);
        Assert.Same(current, await staleWait);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledWait.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task WaitForChangeAsync_RejectsNullAndPreCanceledWaits()
    {
        using ComputeRuntimeStateManager manager = CreateManager();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAsync<ArgumentNullException>(() => manager.WaitForChangeAsync(null));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.WaitForChangeAsync(manager.Current, cancellation.Token));
    }

    [Fact]
    public async Task WaitForChangeAsync_ReturnToEqualStateStillNotifies()
    {
        using ComputeRuntimeStateManager manager = await StartManagerAsync();
        ComputeRuntimeState initial = manager.Current;
        RpcClientWorkerChannel worker = await LinkWorkerAsync("worker", "http", ready: false);
        await WaitForStateAsync(manager, 0, workerCount: 1, httpWorkerCount: 1);
        UnlinkWorker(worker);
        ComputeRuntimeState empty = await WaitForStateAsync(manager, 0, workerCount: 0, httpWorkerCount: 0);

        Assert.Equal(initial, empty);
        Assert.NotSame(initial, empty);
        Task<ComputeRuntimeState> changed = manager.WaitForChangeAsync(initial);
        Assert.True(changed.IsCompletedSuccessfully);
        Assert.Same(empty, await changed);
    }

    [Fact]
    public async Task WaitForChangeAsync_CancelingOneWaitDoesNotAffectOtherWaiters()
    {
        using ComputeRuntimeStateManager manager = await StartManagerAsync();
        ComputeRuntimeState initial = manager.Current;
        using CancellationTokenSource cancellation = new();
        Task<ComputeRuntimeState> first = manager.WaitForChangeAsync(initial);
        Task<ComputeRuntimeState> second = manager.WaitForChangeAsync(initial);
        Task<ComputeRuntimeState> canceled = manager.WaitForChangeAsync(initial, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled.WaitAsync(TestTimeout));
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);

        await LinkWorkerAsync("worker", "http", ready: false);
        ComputeRuntimeState[] changed = await Task.WhenAll(first, second).WaitAsync(TestTimeout);

        Assert.Same(changed[0], changed[1]);
        Assert.Equal(initial.CreatedTime, changed[0].CreatedTime);
        Assert.NotSame(initial, changed[0]);
    }

    [Fact]
    public async Task ApplicationStopping_RecordsZeroCapacityAndSuppressesLaterPositiveCapacity()
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
        ComputeRuntimeState running = await WaitForStateAsync(manager, httpCapacity: 16);

        _applicationStopping.Cancel();
        ComputeRuntimeState stopping = manager.Current;
        Task<ComputeRuntimeState> laterChange = manager.WaitForChangeAsync(stopping);
        await LinkWorkerAsync("second", "http", ready: true);

        // Wait until a recompute sees the new worker; StopAsync then waits for that recompute to finish.
        await secondWorkerCounted.Task.WaitAsync(TestTimeout);
        await manager.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.NotSame(running, stopping);
        Assert.Equal(0, stopping.HttpCapacity);
        ComputeRuntimeState changed = await laterChange.WaitAsync(TestTimeout);
        Assert.Equal(0, changed.HttpCapacity);
        Assert.Equal(2, changed.WorkerCount);
        Assert.Equal(2, changed.HttpWorkerCount);
        Assert.Same(changed, manager.Current);
    }

    [Fact]
    public async Task StopAsync_RecordsZeroCapacityWithoutApplicationStopping()
    {
        SetScriptHostState(ScriptHostState.Running);
        await LinkWorkerAsync("worker", "http", ready: true);
        using ComputeRuntimeStateManager manager = await StartManagerAsync();
        await WaitForStateAsync(manager, httpCapacity: 16);

        await manager.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Equal(0, manager.Current.HttpCapacity);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ExecuteAsync_UnexpectedWaitFailure_WithdrawsCapacityAndPreservesCounts(bool registryWaitFails, bool hostRunning)
    {
        Mock<ILogger<ComputeRuntimeStateManager>> logger = CreateLogger();
        TaskCompletionSource<long> inputChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (registryWaitFails)
        {
            _registry.Setup(registry => registry.WaitForInitializedChannelsChangeAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .Returns((long _, CancellationToken token) => inputChanged.Task.WaitAsync(token));
        }
        else
        {
            _scriptHostManager.Setup(host => host.WaitForStateChangeAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .Returns((long _, CancellationToken token) => inputChanged.Task.WaitAsync(token));
        }

        if (hostRunning)
        {
            SetScriptHostState(ScriptHostState.Running);
        }

        await LinkWorkerAsync("worker", "http", ready: true);
        await LinkWorkerAsync("durable", "durable", ready: true);
        using ComputeRuntimeStateManager manager = await StartManagerAsync(logger.Object);
        ComputeRuntimeState beforeFailure = await WaitForStateAsync(manager, hostRunning ? 16 : 0, workerCount: 2, httpWorkerCount: 1);
        using CancellationTokenSource waiterCancellation = new();
        Task<ComputeRuntimeState> changed = manager.WaitForChangeAsync(beforeFailure, waiterCancellation.Token);

        inputChanged.SetException(new InvalidOperationException("State tracking failed."));

        // A faulted BackgroundService task stops the Host, so the failure must complete the task normally.
        await manager.ExecuteTask.WaitAsync(TestTimeout);
        ComputeRuntimeState afterFailure = manager.Current;
        await manager.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Equal(0, afterFailure.HttpCapacity);
        Assert.Equal(2, afterFailure.WorkerCount);
        Assert.Equal(1, afterFailure.HttpWorkerCount);
        Assert.Same(afterFailure, manager.Current);
        if (hostRunning)
        {
            Assert.Same(afterFailure, await changed.WaitAsync(TestTimeout));
            Assert.NotSame(beforeFailure, afterFailure);
        }
        else
        {
            Assert.Same(beforeFailure, afterFailure);
            Assert.False(changed.IsCompleted);
            waiterCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => changed);
        }

        _applicationLifetime.Verify(lifetime => lifetime.StopApplication(), Times.Never);
        logger.Verify(value => value.Log(
            LogLevel.Error, It.Is<EventId>(eventId => eventId.Id == 902), It.IsAny<It.IsAnyType>(), It.Is<Exception>(exception => exception is InvalidOperationException),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once());
    }

    [Fact]
    public async Task ExecuteAsync_StateReadFailure_WithdrawsCapacityWithoutReadingFailedInputsAgain()
    {
        SetScriptHostState(ScriptHostState.Running);
        await LinkWorkerAsync("worker", "http", ready: true);
        using ComputeRuntimeStateManager manager = await StartManagerAsync();
        await WaitForStateAsync(manager, httpCapacity: 16);
        _scriptHostManager.SetupGet(host => host.State).Throws(new InvalidOperationException("ScriptHost state unavailable."));

        _initializedChannelsSignal.Signal();
        await manager.ExecuteTask.WaitAsync(TestTimeout);

        Assert.Equal(0, manager.Current.HttpCapacity);
        Assert.Equal(1, manager.Current.WorkerCount);
        Assert.Equal(1, manager.Current.HttpWorkerCount);
        await manager.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task ExecuteAsync_RegistryDisposal_WithdrawsCapacityWhenConfiguredUtcClockFails()
    {
        Mock<ILogger<ComputeRuntimeStateManager>> logger = CreateLogger();
        TaskCompletionSource<long> registryChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _registry.Setup(registry => registry.WaitForInitializedChannelsChangeAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns((long _, CancellationToken token) => registryChanged.Task.WaitAsync(token));
        SetScriptHostState(ScriptHostState.Running);
        await LinkWorkerAsync("worker", "http", ready: true);
        using ComputeRuntimeStateManager manager = await StartManagerAsync(logger.Object);
        ComputeRuntimeState running = await WaitForStateAsync(manager, httpCapacity: 16);
        InvalidOperationException clockFailure = new("Clock unavailable.");
        _timeProvider.Setup(provider => provider.GetUtcNow()).Throws(clockFailure);

        registryChanged.SetException(new ObjectDisposedException(nameof(IWorkerChannelRegistry)));
        await manager.ExecuteTask.WaitAsync(TestTimeout);
        await manager.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Equal(0, manager.Current.HttpCapacity);
        Assert.Equal(1, manager.Current.WorkerCount);
        Assert.Equal(1, manager.Current.HttpWorkerCount);
        Assert.NotSame(running, manager.Current);
        Assert.Equal(TimeSpan.Zero, manager.Current.CreatedTime.Offset);
        logger.Verify(value => value.Log(
            LogLevel.Warning, It.Is<EventId>(eventId => eventId.Id == 905), It.IsAny<It.IsAnyType>(), clockFailure,
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once());
    }

    [Fact]
    public async Task CreatedTime_UsesUtcClockAndNotifiesDespiteEqualOrEarlierTime()
    {
        SetScriptHostState(ScriptHostState.Running);
        using ComputeRuntimeStateManager manager = await StartManagerAsync();
        ComputeRuntimeState initial = manager.Current;
        Task<ComputeRuntimeState> firstChange = manager.WaitForChangeAsync(initial);

        RpcClientWorkerChannel first = await LinkWorkerAsync("first", "http", ready: true);
        ComputeRuntimeState sameTime = await firstChange.WaitAsync(TestTimeout);
        Task<ComputeRuntimeState> secondChange = manager.WaitForChangeAsync(sameTime);
        SetUtcTime(StartTime.AddMinutes(-5));
        UnlinkWorker(first);
        ComputeRuntimeState earlier = await secondChange.WaitAsync(TestTimeout);
        Task<ComputeRuntimeState> thirdChange = manager.WaitForChangeAsync(earlier);
        DateTimeOffset laterTime = StartTime.AddMinutes(5).AddTicks(1234);
        SetUtcTime(laterTime);
        await LinkWorkerAsync("second", "http", ready: true);
        ComputeRuntimeState later = await thirdChange.WaitAsync(TestTimeout);

        Assert.Equal(StartTime, initial.CreatedTime);
        Assert.Equal(initial.CreatedTime, sameTime.CreatedTime);
        Assert.NotSame(initial, sameTime);
        Assert.Equal(16, sameTime.HttpCapacity);
        Assert.Equal(StartTime.AddMinutes(-5), earlier.CreatedTime);
        Assert.Equal(0, earlier.HttpCapacity);
        Assert.Equal(laterTime, later.CreatedTime);
        Assert.Equal(16, later.HttpCapacity);
    }

    [Fact]
    public async Task CreatedTime_WithdrawalCapturesUtcOnceAndRetainsCounts()
    {
        Mock<ILogger<ComputeRuntimeStateManager>> logger = CreateLogger();
        SetScriptHostState(ScriptHostState.Running);
        await LinkWorkerAsync("worker", "http", ready: true);
        using ComputeRuntimeStateManager manager = await StartManagerAsync(logger.Object);
        ComputeRuntimeState running = await WaitForStateAsync(manager, httpCapacity: 16);
        DateTimeOffset stoppingTime = StartTime.AddMinutes(2);
        SetUtcTime(stoppingTime);

        await manager.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
        ComputeRuntimeState stopped = manager.Current;
        SetUtcTime(stoppingTime.AddMinutes(1));
        await manager.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Equal(0, stopped.HttpCapacity);
        Assert.Equal(running.WorkerCount, stopped.WorkerCount);
        Assert.Equal(running.HttpWorkerCount, stopped.HttpWorkerCount);
        Assert.Equal(stoppingTime, stopped.CreatedTime);
        Assert.Equal(StartTime, running.CreatedTime);
        Assert.Same(stopped, manager.Current);
        logger.Verify(value => value.Log(
            LogLevel.Information, It.Is<EventId>(eventId => eventId.Id == 904), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once());
    }

    [Theory]
    [InlineData(-1000)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task CreatedTime_NewManagerUsesCurrentUtcWithoutOrderingAgainstPreviousInstance(int elapsedMilliseconds)
    {
        SetScriptHostState(ScriptHostState.Running);
        await LinkWorkerAsync("worker", "http", ready: true);
        using ComputeRuntimeStateManager previousManager = await StartManagerAsync();
        await WaitForStateAsync(previousManager, httpCapacity: 16);
        await previousManager.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
        ComputeRuntimeState previous = previousManager.Current;
        DateTimeOffset now = StartTime.AddMilliseconds(elapsedMilliseconds);
        SetUtcTime(now);

        using ComputeRuntimeStateManager manager = CreateManager();
        ComputeRuntimeState initial = manager.Current;
        await manager.StartAsync(CancellationToken.None);
        ComputeRuntimeState running = await WaitForStateAsync(manager, httpCapacity: 16);

        Assert.Equal(StartTime, previous.CreatedTime);
        Assert.Equal(now, initial.CreatedTime);
        Assert.Equal(now, running.CreatedTime);
        Assert.NotSame(initial, running);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (IAsyncDisposable worker in _workers)
        {
            await worker.DisposeAsync();
        }

        _applicationStopping.Dispose();
    }

    private async Task<ComputeRuntimeStateManager> StartManagerAsync(ILogger<ComputeRuntimeStateManager> logger = null)
    {
        ComputeRuntimeStateManager manager = CreateManager(logger);
        await manager.StartAsync(CancellationToken.None);

        return manager;
    }

    private ComputeRuntimeStateManager CreateManager(ILogger<ComputeRuntimeStateManager> logger = null)
        => new(
            _registry.Object,
            _scriptHostManager.Object,
            _applicationLifetime.Object,
            _timeProvider.Object,
            logger ?? NullLogger<ComputeRuntimeStateManager>.Instance);

    private RpcClientFunctionInvocationDispatcher CreateDispatcher()
    {
        RpcClientFunctionInvocationDispatcher dispatcher = new(
            _registry.Object, Options.Create(new ScriptJobHostOptions()), Options.Create(new ManagedDependencyOptions()),
            NullLogger<RpcClientFunctionInvocationDispatcher>.Instance);
        _scriptServices.Setup(provider => provider.GetService(typeof(IRpcClientFunctionInvocationDispatcher))).Returns(dispatcher);

        return dispatcher;
    }

    private static Mock<ILogger<ComputeRuntimeStateManager>> CreateLogger()
    {
        Mock<ILogger<ComputeRuntimeStateManager>> logger = new();
        logger.Setup(value => value.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        return logger;
    }

    private static async Task<ComputeRuntimeState> WaitForStateAsync(
        ComputeRuntimeStateManager manager, long httpCapacity, int? workerCount = null, int? httpWorkerCount = null)
    {
        using CancellationTokenSource timeoutSource = new(TestTimeout);
        ComputeRuntimeState state = manager.Current;
        while (state.HttpCapacity != httpCapacity ||
            (workerCount is { } total && state.WorkerCount != total) ||
            (httpWorkerCount is { } http && state.HttpWorkerCount != http))
        {
            state = await manager.WaitForChangeAsync(state, timeoutSource.Token);
        }

        return state;
    }

    private async Task<RpcClientWorkerChannel> LinkWorkerAsync(
        string workerId, string functionGroupName, bool ready, IMetricsLogger metricsLogger = null)
    {
        ClientWorkerChannelTestHarness worker =
            await ClientWorkerChannelTestHarness.CreateAsync(workerId, metricsLogger: metricsLogger, functionGroupName: functionGroupName);
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

    private void SetUtcTime(DateTimeOffset now) => Volatile.Write(ref _utcTicks, now.UtcTicks);

    private void SetScriptHostState(ScriptHostState state) => _scriptHostStateSignal.Set(state);

    private WorkerChannel[] GetChannels()
    {
        lock (_channelsLock)
        {
            return [.. _channels];
        }
    }
}
