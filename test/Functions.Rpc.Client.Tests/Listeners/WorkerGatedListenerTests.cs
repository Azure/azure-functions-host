// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Timers;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed class WorkerGatedListenerTests : IDisposable
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private readonly FakeTimeProvider _timeProvider = new();
    private readonly Mock<IListener> _initial = new();
    private readonly Mock<IListenerFactory> _factory = new();
    private readonly WorkerGatedListenerManager _manager;

    public WorkerGatedListenerTests()
    {
        // A disposed registry ends the reconciliation loop, so these tests drive generations directly.
        Mock<IWorkerChannelRegistry> registry = new();
        registry.Setup(r => r.GetChangeTask()).Throws(new ObjectDisposedException(nameof(IWorkerChannelRegistry)));
        _manager = new(registry.Object, _timeProvider, NullLogger<WorkerGatedListenerManager>.Instance);
    }

    public void Dispose() => _manager.Dispose();

    [Fact]
    public async Task StartAsync_DoesNotStartListener()
    {
        using WorkerGatedListener listener = CreateListener();

        await listener.StartAsync(CancellationToken.None);

        Assert.False(listener.IsListening);
        _initial.Verify(l => l.StartAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartListeningAsync_BeforeHostStart_DoesNotStartListener()
    {
        using WorkerGatedListener listener = CreateListener();

        Assert.True(await listener.StartListeningAsync(CancellationToken.None));

        Assert.False(listener.IsListening);
        _initial.Verify(l => l.StartAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartListeningAsync_AfterStop_CreatesNewGeneration()
    {
        Mock<IListener> second = new();
        _factory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(second.Object);
        using WorkerGatedListener listener = CreateListener();
        await listener.StartAsync(CancellationToken.None);

        Assert.True(await listener.StartListeningAsync(CancellationToken.None));
        Assert.True(listener.IsListening);
        _initial.Verify(l => l.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
        _factory.Verify(f => f.CreateAsync(It.IsAny<CancellationToken>()), Times.Never);

        await listener.StopListeningAsync(CancellationToken.None);
        Assert.False(listener.IsListening);
        _initial.Verify(l => l.StopAsync(It.IsAny<CancellationToken>()), Times.Once);

        Assert.True(await listener.StartListeningAsync(CancellationToken.None));
        Assert.True(listener.IsListening);
        _initial.Verify(l => l.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
        _factory.Verify(f => f.CreateAsync(It.IsAny<CancellationToken>()), Times.Once);
        second.Verify(l => l.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartListeningAsync_WhileListening_DoesNotStartAgain()
    {
        using WorkerGatedListener listener = CreateListener();
        await listener.StartAsync(CancellationToken.None);

        await listener.StartListeningAsync(CancellationToken.None);
        await listener.StartListeningAsync(CancellationToken.None);

        _initial.Verify(l => l.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
        _factory.Verify(f => f.CreateAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StopListeningAsync_StartOnly_KeepsListenerRunning()
    {
        using WorkerGatedListener listener = CreateListener(startOnly: true);
        await listener.StartAsync(CancellationToken.None);
        await listener.StartListeningAsync(CancellationToken.None);

        await listener.StopListeningAsync(CancellationToken.None);

        Assert.True(listener.IsListening);
        _initial.Verify(l => l.StopAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StopListeningAsync_Failure_RetainsGenerationUntilStopSucceeds()
    {
        Mock<IListener> replacement = new();
        _factory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(replacement.Object);
        _initial.SetupSequence(l => l.StopAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("stop failed"))
            .ThrowsAsync(new InvalidOperationException("stop failed again"))
            .Returns(Task.CompletedTask);
        using WorkerGatedListener listener = CreateListener();
        await listener.StartAsync(CancellationToken.None);
        await listener.StartListeningAsync(CancellationToken.None);

        Assert.False(await listener.StopListeningAsync(CancellationToken.None));
        Assert.True(listener.IsListening);
        Assert.False(await listener.StartListeningAsync(CancellationToken.None));
        _factory.Verify(f => f.CreateAsync(It.IsAny<CancellationToken>()), Times.Never);
        _initial.Verify(l => l.Dispose(), Times.Never);

        Assert.True(await listener.StartListeningAsync(CancellationToken.None));
        _initial.Verify(l => l.StopAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
        replacement.Verify(l => l.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancel_AfterStopFailure_ForwardsToRetainedGeneration()
    {
        _initial.Setup(l => l.StopAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("stop failed"));
        WorkerGatedListener listener = CreateListener();
        await listener.StartAsync(CancellationToken.None);
        await listener.StartListeningAsync(CancellationToken.None);
        Assert.False(await listener.StopListeningAsync(CancellationToken.None));

        listener.Cancel();

        _initial.Verify(l => l.Cancel(), Times.Once);
        Assert.Throws<InvalidOperationException>(listener.Dispose);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartListeningAsync_CanceledDuringNonCooperativeStart_RespectsStartOnly(bool startOnly)
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource finishStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _initial.Setup(l => l.StartAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            started.TrySetResult();
            return finishStart.Task;
        });
        using WorkerGatedListener listener = CreateListener(startOnly);
        using CancellationTokenSource cancellation = new();
        await listener.StartAsync(CancellationToken.None);

        Task<bool> start = listener.StartListeningAsync(cancellation.Token);
        await started.Task.WaitAsync(TestTimeout);
        cancellation.Cancel();
        finishStart.SetResult();

        Assert.Equal(startOnly, await start.WaitAsync(TestTimeout));
        Assert.Equal(startOnly, listener.IsListening);
        _initial.Verify(l => l.StopAsync(It.IsAny<CancellationToken>()), startOnly ? Times.Never : Times.Once);
        _initial.Verify(l => l.Cancel(), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartListeningAsync_CanceledTimerStart_ReleasesSingletonLeaseBeforeRestart(bool startThrows)
    {
        Mock<IHostIdProvider> hostId = new();
        hostId.Setup(p => p.GetHostIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync("worker-gated-timer-test");
#pragma warning disable CS0618 // The WebJobs singleton lock interface is marked as a preview API.
        int leaseHeld = 0;
        Mock<IDistributedLock> lease = new();
        lease.Setup(l => l.LockId).Returns("timer");
        Mock<IDistributedLockManager> lockManager = new();
        lockManager.Setup(l => l.TryLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.CompareExchange(ref leaseHeld, 1, 0) == 0 ? lease.Object : null);
        lockManager.Setup(l => l.ReleaseLockAsync(It.IsAny<IDistributedLock>(), It.IsAny<CancellationToken>()))
            .Returns((IDistributedLock _, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                Interlocked.Exchange(ref leaseHeld, 0);
                return Task.CompletedTask;
            });
        using IHost webJobs = new HostBuilder().ConfigureWebJobs()
            .ConfigureServices(services => services.AddSingleton(hostId.Object).AddSingleton(lockManager.Object))
            .Build();
#pragma warning restore CS0618
        IHostSingletonManager singleton = webJobs.Services.GetRequiredService<IHostSingletonManager>();
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource finishStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken startToken = default;
        Mock<IListener> timer = new();
        timer.Setup(l => l.StartAsync(It.IsAny<CancellationToken>())).Returns(async (CancellationToken token) =>
        {
            startToken = token;
            started.TrySetResult();
            await finishStart.Task;
            if (startThrows)
            {
                throw new InvalidOperationException("start failed");
            }
        });
        timer.Setup(l => l.Cancel()).Callback(() => canceled.TrySetResult());
        timer.Setup(l => l.StopAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The listener has not yet been started or has already been stopped."));
        IListener initial = singleton.CreateHostSingletonListener(timer.Object, "timer");
        Mock<IListener> replacement = new();
        _factory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(singleton.CreateHostSingletonListener(replacement.Object, "timer"));
        using WorkerGatedListener listener = new(initial, _factory.Object, "TestFunction", "function:TestFunction", false, _manager,
            _timeProvider, NullLogger.Instance, cancelDuringStart: true);
        using CancellationTokenSource cancellation = new();
        await listener.StartAsync(CancellationToken.None);

        Task<bool> start = listener.StartListeningAsync(cancellation.Token);
        await started.Task.WaitAsync(TestTimeout);
        cancellation.Cancel();
        await canceled.Task.WaitAsync(TestTimeout);
        Assert.False(start.IsCompleted);
        Assert.False(startToken.IsCancellationRequested);
        finishStart.SetResult();

        Assert.False(await start.WaitAsync(TestTimeout));
        timer.Verify(l => l.StopAsync(It.IsAny<CancellationToken>()), startThrows ? Times.Never : Times.Once);
        timer.Verify(l => l.Dispose(), startThrows ? Times.Never : Times.Once);
        Assert.True(await listener.StartListeningAsync(CancellationToken.None));
        replacement.Verify(l => l.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
        await listener.StopListeningAsync(CancellationToken.None);
        listener.Dispose();
        timer.Verify(l => l.Dispose(), Times.Once);
        replacement.Verify(l => l.Dispose(), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartListeningAsync_CanceledRealTimerStart_ReleasesLeaseOnlyAfterWorkCompletes(bool activeInvocation)
    {
        TaskCompletionSource readingStatus = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<ScheduleStatus> status = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource innerStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource finishStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource stopping = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource finishInvocation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<ScheduleMonitor> monitor = new();
        monitor.Setup(m => m.GetStatusAsync(It.IsAny<string>())).Returns(() =>
        {
            readingStatus.TrySetResult();
            return status.Task;
        });
        monitor.Setup(m => m.CheckPastDueAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<TimerSchedule>(),
            It.IsAny<ScheduleStatus>())).ReturnsAsync(TimeSpan.Zero);
        Mock<ITypeLocator> locator = new();
        locator.Setup(l => l.GetTypes()).Returns([typeof(TimerJob)]);
        Mock<IHostIdProvider> hostId = new();
        hostId.Setup(p => p.GetHostIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync("worker-gated-timer-test");
        WorkerGatedListener gated = null;
        CancellationToken stopToken = default;
        Mock<IListenerDecorator> decorator = new();
        decorator.Setup(d => d.Decorate(It.IsAny<ListenerDecoratorContext>()))
            .Returns((ListenerDecoratorContext context) =>
            {
                IListener inner = context.Listener;
                Mock<IListener> observed = new();
                observed.Setup(l => l.StartAsync(It.IsAny<CancellationToken>())).Returns(async (CancellationToken token) =>
                {
                    await inner.StartAsync(token);
                    innerStarted.TrySetResult();
                    if (activeInvocation)
                    {
                        await finishStart.Task;
                    }
                });
                observed.Setup(l => l.StopAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken token) =>
                {
                    stopToken = token;
                    stopping.TrySetResult();
                    return inner.StopAsync(token);
                });
                observed.Setup(l => l.Cancel()).Callback(() =>
                {
                    inner.Cancel();
                    canceled.TrySetResult();
                });
                observed.Setup(l => l.Dispose()).Callback(inner.Dispose);
                gated = new(observed.Object, context.FunctionDefinition.ListenerFactory, "TimerJob", "function:TimerJob", false, _manager,
                    _timeProvider, NullLogger.Instance, cancelDuringStart: true);
                return gated;
            });
        TimerJob.Invocation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TimerJob.InvocationCompletion = finishInvocation.Task;
        if (activeInvocation)
        {
            status.SetResult(null);
        }

        using IHost webJobs = new HostBuilder().ConfigureWebJobs(builder => builder.AddTimers())
            .ConfigureServices(services => services.AddSingleton(locator.Object).AddSingleton(hostId.Object)
                .AddSingleton(monitor.Object).AddSingleton(decorator.Object))
            .Build();
        await webJobs.StartAsync();
        try
        {
            Assert.NotNull(gated);
            using CancellationTokenSource cancellation = new();
            Task<bool> start = gated.StartListeningAsync(cancellation.Token);
            await readingStatus.Task.WaitAsync(TestTimeout);
            if (activeInvocation)
            {
                await Task.WhenAll(innerStarted.Task, TimerJob.Invocation.Task).WaitAsync(TestTimeout);
            }

            cancellation.Cancel();
            await canceled.Task.WaitAsync(TestTimeout);
            status.TrySetResult(null);
            finishStart.TrySetResult();
            await stopping.Task.WaitAsync(TestTimeout);

            if (activeInvocation)
            {
                Assert.False(start.IsCompleted);
                Task<bool> restart = gated.StartListeningAsync(CancellationToken.None);
                Assert.False(restart.IsCompleted);
                _timeProvider.Advance(WorkerGatedListener.FailedStartStopTimeout + TimeSpan.FromSeconds(1));
                Assert.False(stopToken.IsCancellationRequested);
                finishInvocation.TrySetResult();
                Assert.False(await start.WaitAsync(TestTimeout));
                Assert.True(await restart.WaitAsync(TestTimeout));
            }
            else
            {
                Assert.False(await start.WaitAsync(TestTimeout));
                Assert.False(TimerJob.Invocation.Task.IsCompleted);
                finishInvocation.TrySetResult();
                Assert.True(await gated.StartListeningAsync(CancellationToken.None));
            }

            await TimerJob.Invocation.Task.WaitAsync(TestTimeout);
            await gated.StopListeningAsync(CancellationToken.None);
        }
        finally
        {
            status.TrySetResult(null);
            finishStart.TrySetResult();
            finishInvocation.TrySetResult();
            await webJobs.StopAsync();
        }
    }

    [Fact]
    public async Task StartListeningAsync_CanceledTimerCleanupFailure_PreventsAnotherGeneration()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource finishStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        InvalidOperationException stopFailure = new("The listener has not yet been started or has already been stopped.");
        InvalidOperationException disposeFailure = new("dispose failed");
        Mock<ILogger> logger = new();
        logger.Setup(l => l.IsEnabled(LogLevel.Error)).Returns(true);
        _initial.Setup(l => l.StartAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            started.TrySetResult();
            return finishStart.Task;
        });
        _initial.Setup(l => l.Cancel()).Callback(() => canceled.TrySetResult());
        _initial.Setup(l => l.StopAsync(It.IsAny<CancellationToken>())).ThrowsAsync(stopFailure);
        _initial.Setup(l => l.Dispose()).Throws(disposeFailure);
        using WorkerGatedListener listener = CreateListener(logger: logger.Object, cancelDuringStart: true);
        using CancellationTokenSource cancellation = new();
        await listener.StartAsync(CancellationToken.None);

        Task<bool> start = listener.StartListeningAsync(cancellation.Token);
        await started.Task.WaitAsync(TestTimeout);
        cancellation.Cancel();
        await canceled.Task.WaitAsync(TestTimeout);
        finishStart.SetResult();

        Assert.False(await start.WaitAsync(TestTimeout));
        Assert.False(await listener.StartListeningAsync(CancellationToken.None));
        _factory.Verify(f => f.CreateAsync(It.IsAny<CancellationToken>()), Times.Never);
        logger.Verify(l => l.Log(LogLevel.Error, It.Is<EventId>(id => id.Id == 5), It.IsAny<It.IsAnyType>(),
            It.Is<Exception>(exception => exception is AggregateException
                && ((AggregateException)exception).InnerExceptions.Contains(stopFailure)
                && ((AggregateException)exception).InnerExceptions.Contains(disposeFailure)),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
        listener.Dispose();
        _initial.Verify(l => l.Dispose(), Times.Once);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task StartListeningAsync_TimerCleanupIsUncertain_DoesNotReleasePossiblyActiveGeneration(
        bool useSecondGeneration, bool cancellationFails, bool invalidStopFailure)
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource finishStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource cancelAttempted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<IListener> second = new();
        Mock<IListener> failing = useSecondGeneration ? second : _initial;
        failing.Setup(l => l.StartAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            started.TrySetResult();
            return finishStart.Task;
        });
        failing.Setup(l => l.Cancel()).Callback(() =>
        {
            cancelAttempted.TrySetResult();
            if (cancellationFails)
            {
                throw new InvalidOperationException("cancel failed");
            }
        });
        Exception stopFailure = cancellationFails || invalidStopFailure
            ? new InvalidOperationException("stop failed")
            : new TimeoutException("stop failed");
        failing.Setup(l => l.StopAsync(It.IsAny<CancellationToken>())).ThrowsAsync(stopFailure);
        _factory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(second.Object);
        using WorkerGatedListener listener = CreateListener(cancelDuringStart: true);
        using CancellationTokenSource cancellation = new();
        await listener.StartAsync(CancellationToken.None);
        if (useSecondGeneration)
        {
            await listener.StartListeningAsync(CancellationToken.None);
            await listener.StopListeningAsync(CancellationToken.None);
        }

        Task<bool> start = listener.StartListeningAsync(cancellation.Token);
        await started.Task.WaitAsync(TestTimeout);
        cancellation.Cancel();
        await cancelAttempted.Task.WaitAsync(TestTimeout);
        finishStart.SetResult();

        Assert.False(await start.WaitAsync(TestTimeout));
        Assert.False(await listener.StartListeningAsync(CancellationToken.None));
        failing.Verify(l => l.Dispose(), Times.Never);

        Assert.Same(stopFailure, Assert.ThrowsAny<Exception>(listener.Dispose));
        failing.Verify(l => l.Dispose(), Times.Never);
        if (useSecondGeneration)
        {
            _initial.Verify(l => l.Dispose(), Times.Once);
        }
    }

    [Fact]
    public async Task StopAsync_DuringNonCooperativeStartOnlyStart_StopsListener()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource finishStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _initial.Setup(l => l.StartAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            started.TrySetResult();
            return finishStart.Task;
        });
        using WorkerGatedListener listener = CreateListener(startOnly: true);
        await listener.StartAsync(CancellationToken.None);

        Task<bool> start = listener.StartListeningAsync(CancellationToken.None);
        await started.Task.WaitAsync(TestTimeout);
        Task stop = listener.StopAsync(CancellationToken.None);
        finishStart.SetResult();

        Assert.False(await start.WaitAsync(TestTimeout));
        await stop.WaitAsync(TestTimeout);
        Assert.False(listener.IsListening);
        _initial.Verify(l => l.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartListeningAsync_WorkerLossDuringStartOnlyStart_DoesNotCancelSharedStart()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource finishStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _initial.Setup(l => l.StartAsync(It.IsAny<CancellationToken>())).Returns(async (CancellationToken token) =>
        {
            started.TrySetResult();
            await finishStart.Task.WaitAsync(token);
        });
        using WorkerGatedListener listener = CreateListener(startOnly: true);
        using CancellationTokenSource cancellation = new();
        await listener.StartAsync(CancellationToken.None);

        Task<bool> start = listener.StartListeningAsync(cancellation.Token);
        await started.Task.WaitAsync(TestTimeout);
        cancellation.Cancel();
        Assert.False(start.IsCompleted);
        finishStart.SetResult();

        Assert.True(await start.WaitAsync(TestTimeout));
        Assert.True(listener.IsListening);
        _initial.Verify(l => l.StopAsync(It.IsAny<CancellationToken>()), Times.Never);
        _factory.Verify(f => f.CreateAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartListeningAsync_CanceledBeforeLock_ReturnsFalse()
    {
        using WorkerGatedListener listener = CreateListener();
        using CancellationTokenSource cancellation = new();
        await listener.StartAsync(CancellationToken.None);
        cancellation.Cancel();

        Assert.False(await listener.StartListeningAsync(cancellation.Token));
        _initial.Verify(l => l.StartAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartListeningAsync_CanceledDuringFactory_DoesNotStartGeneration()
    {
        TaskCompletionSource creating = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<IListener> finishCreate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<IListener> second = new();
        _factory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            creating.TrySetResult();
            return finishCreate.Task;
        });
        using WorkerGatedListener listener = CreateListener();
        using CancellationTokenSource cancellation = new();
        await listener.StartAsync(CancellationToken.None);
        await listener.StartListeningAsync(CancellationToken.None);
        await listener.StopListeningAsync(CancellationToken.None);

        Task<bool> start = listener.StartListeningAsync(cancellation.Token);
        await creating.Task.WaitAsync(TestTimeout);
        cancellation.Cancel();
        finishCreate.SetResult(second.Object);

        Assert.False(await start.WaitAsync(TestTimeout));
        second.Verify(l => l.StartAsync(It.IsAny<CancellationToken>()), Times.Never);
        second.Verify(l => l.StopAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartListeningAsync_CanceledBeforeInitialStart_ReusesInitialListener(bool startOnly)
    {
        using CancellationTokenSource cancellation = new();
        Mock<ILogger> logger = new();
        logger.Setup(l => l.IsEnabled(LogLevel.Information)).Returns(true);
        logger.Setup(l => l.Log(LogLevel.Information, It.Is<EventId>(id => id.Id == 0), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception, string>>())).Callback(cancellation.Cancel);
        using WorkerGatedListener listener = CreateListener(startOnly, logger.Object);
        await listener.StartAsync(CancellationToken.None);

        Assert.False(await listener.StartListeningAsync(cancellation.Token));
        _initial.Verify(l => l.StartAsync(It.IsAny<CancellationToken>()), Times.Never);
        _initial.Verify(l => l.StopAsync(It.IsAny<CancellationToken>()), Times.Never);

        Assert.True(await listener.StartListeningAsync(CancellationToken.None));
        _initial.Verify(l => l.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
        _factory.Verify(f => f.CreateAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartListeningAsync_StartFailure_ReturnsFalseAndRetriesWithNewGeneration()
    {
        Mock<IListener> second = new();
        _factory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(second.Object);
        _initial.Setup(l => l.StartAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));
        using WorkerGatedListener listener = CreateListener();
        await listener.StartAsync(CancellationToken.None);

        Assert.False(await listener.StartListeningAsync(CancellationToken.None));
        Assert.False(listener.IsListening);

        Assert.True(await listener.StartListeningAsync(CancellationToken.None));
        Assert.True(listener.IsListening);
        second.Verify(l => l.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartListeningAsync_FailedStartAndStop_RetainsGenerationWithoutRestarting()
    {
        _initial.Setup(l => l.StartAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("start failed"));
        InvalidOperationException stopFailure = new("stop failed");
        _initial.Setup(l => l.StopAsync(It.IsAny<CancellationToken>())).ThrowsAsync(stopFailure);
        WorkerGatedListener listener = CreateListener();
        await listener.StartAsync(CancellationToken.None);

        Assert.False(await listener.StartListeningAsync(CancellationToken.None));
        Assert.False(await listener.StartListeningAsync(CancellationToken.None));
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => listener.StopAsync(CancellationToken.None));
        Assert.Same(stopFailure, exception.InnerException);
        _factory.Verify(f => f.CreateAsync(It.IsAny<CancellationToken>()), Times.Never);
        _initial.Verify(l => l.Dispose(), Times.Never);
        Assert.Same(stopFailure, Assert.Throws<InvalidOperationException>(listener.Dispose));
    }

    [Fact]
    public async Task StopListeningAsync_DisposesRetiredGenerationAfterDelay()
    {
        TaskCompletionSource secondDisposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<IListener> second = new();
        Mock<IListener> third = new();
        second.Setup(l => l.Dispose()).Callback(() => secondDisposed.TrySetResult());
        _factory.SetupSequence(f => f.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(second.Object)
            .ReturnsAsync(third.Object);
        using WorkerGatedListener listener = CreateListener();
        await listener.StartAsync(CancellationToken.None);
        await listener.StartListeningAsync(CancellationToken.None);
        await listener.StopListeningAsync(CancellationToken.None);
        await listener.StartListeningAsync(CancellationToken.None);

        await listener.StopListeningAsync(CancellationToken.None);
        await listener.StartListeningAsync(CancellationToken.None);
        _timeProvider.Advance(WorkerGatedListener.RetiredListenerDisposalDelay - TimeSpan.FromSeconds(1));
        Assert.False(secondDisposed.Task.IsCompleted);

        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        await secondDisposed.Task.WaitAsync(TestTimeout);
        _initial.Verify(l => l.Dispose(), Times.Never);
        third.Verify(l => l.Dispose(), Times.Never);
        Assert.True(listener.IsListening);
    }

    [Fact]
    public async Task StopAsync_StopsCurrentAndPreventsLaterStarts()
    {
        using WorkerGatedListener listener = CreateListener();
        await listener.StartAsync(CancellationToken.None);
        await listener.StartListeningAsync(CancellationToken.None);

        await listener.StopAsync(CancellationToken.None);

        Assert.False(listener.IsListening);
        _initial.Verify(l => l.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(await listener.StartListeningAsync(CancellationToken.None));
        Assert.False(listener.IsListening);
        _factory.Verify(f => f.CreateAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StopAsync_Failure_PropagatesAndRetainsGeneration()
    {
        InvalidOperationException failure = new("stop failed");
        _initial.SetupSequence(l => l.StopAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure)
            .Returns(Task.CompletedTask);
        using WorkerGatedListener listener = CreateListener();
        await listener.StartAsync(CancellationToken.None);
        await listener.StartListeningAsync(CancellationToken.None);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => listener.StopAsync(CancellationToken.None));

        Assert.Same(failure, exception);
        Assert.True(listener.IsListening);
        _initial.Verify(l => l.Dispose(), Times.Never);

        await listener.StopAsync(CancellationToken.None);
        Assert.False(listener.IsListening);
        _initial.Verify(l => l.StopAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Cancel_DuringStop_ForwardsToStoppingGeneration()
    {
        TaskCompletionSource stopping = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource finishStop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<IListener> second = new();
        second.Setup(l => l.StopAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            stopping.TrySetResult();
            return finishStop.Task;
        });
        _factory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(second.Object);
        using WorkerGatedListener listener = CreateListener();
        await listener.StartAsync(CancellationToken.None);
        await listener.StartListeningAsync(CancellationToken.None);
        await listener.StopListeningAsync(CancellationToken.None);
        await listener.StartListeningAsync(CancellationToken.None);

        Task stop = listener.StopListeningAsync(CancellationToken.None);
        await stopping.Task.WaitAsync(TestTimeout);
        Assert.True(listener.IsListening);
        listener.Cancel();

        second.Verify(l => l.Cancel(), Times.Once);
        _initial.Verify(l => l.Cancel(), Times.Never);
        finishStop.SetResult();
        await stop.WaitAsync(TestTimeout);
        Assert.False(listener.IsListening);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispose_DuringStop_WaitsToDisposeStoppingGeneration(bool useSecondGeneration)
    {
        TaskCompletionSource stopping = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource finishStop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource initialDisposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource secondDisposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<IListener> second = new();
        Mock<IListener> stoppingListener = useSecondGeneration ? second : _initial;
        _initial.Setup(l => l.Dispose()).Callback(() => initialDisposed.TrySetResult());
        second.Setup(l => l.Dispose()).Callback(() => secondDisposed.TrySetResult());
        stoppingListener.Setup(l => l.StopAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            stopping.TrySetResult();
            return finishStop.Task;
        });
        _factory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(second.Object);
        WorkerGatedListener listener = CreateListener();
        await listener.StartAsync(CancellationToken.None);
        await listener.StartListeningAsync(CancellationToken.None);
        if (useSecondGeneration)
        {
            await listener.StopListeningAsync(CancellationToken.None);
            await listener.StartListeningAsync(CancellationToken.None);
        }

        Task stop = listener.StopListeningAsync(CancellationToken.None);
        await stopping.Task.WaitAsync(TestTimeout);
        listener.Dispose();
        listener.Dispose();

        Assert.False(initialDisposed.Task.IsCompleted);
        stoppingListener.Verify(l => l.Dispose(), Times.Never);
        finishStop.SetResult();
        await stop.WaitAsync(TestTimeout);
        await initialDisposed.Task.WaitAsync(TestTimeout);
        if (useSecondGeneration)
        {
            await secondDisposed.Task.WaitAsync(TestTimeout);
            second.Verify(l => l.Dispose(), Times.Once);
        }

        _initial.Verify(l => l.Dispose(), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispose_DuringStart_WaitsToDisposeStartingGeneration(bool useSecondGeneration)
    {
        TaskCompletionSource starting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource finishStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource initialDisposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource secondDisposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<IListener> second = new();
        Mock<IListener> startingListener = useSecondGeneration ? second : _initial;
        startingListener.Setup(l => l.StartAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            starting.TrySetResult();
            return finishStart.Task;
        });
        _initial.Setup(l => l.Dispose()).Callback(() => initialDisposed.TrySetResult());
        second.Setup(l => l.Dispose()).Callback(() => secondDisposed.TrySetResult());
        _factory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(second.Object);
        WorkerGatedListener listener = CreateListener();
        await listener.StartAsync(CancellationToken.None);
        if (useSecondGeneration)
        {
            await listener.StartListeningAsync(CancellationToken.None);
            await listener.StopListeningAsync(CancellationToken.None);
        }

        Task<bool> start = listener.StartListeningAsync(CancellationToken.None);
        await starting.Task.WaitAsync(TestTimeout);
        listener.Dispose();
        Assert.False(initialDisposed.Task.IsCompleted);
        startingListener.Verify(l => l.Dispose(), Times.Never);

        finishStart.SetResult();
        Assert.False(await start.WaitAsync(TestTimeout));
        await initialDisposed.Task.WaitAsync(TestTimeout);
        if (useSecondGeneration)
        {
            await secondDisposed.Task.WaitAsync(TestTimeout);
            second.Verify(l => l.Dispose(), Times.Once);
        }

        startingListener.Verify(l => l.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
        _initial.Verify(l => l.Dispose(), Times.Once);
    }

    [Fact]
    public async Task Dispose_DuringStop_DisposesPreviouslyRetiredGenerations()
    {
        TaskCompletionSource stopping = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource finishStop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource initialDisposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource thirdDisposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<IListener> second = new();
        Mock<IListener> third = new();
        _initial.Setup(l => l.Dispose()).Callback(() => initialDisposed.TrySetResult());
        third.Setup(l => l.Dispose()).Callback(() => thirdDisposed.TrySetResult());
        third.Setup(l => l.StopAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            stopping.TrySetResult();
            return finishStop.Task;
        });
        _factory.SetupSequence(f => f.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(second.Object)
            .ReturnsAsync(third.Object);
        WorkerGatedListener listener = CreateListener();
        await listener.StartAsync(CancellationToken.None);
        await listener.StartListeningAsync(CancellationToken.None);
        await listener.StopListeningAsync(CancellationToken.None);
        await listener.StartListeningAsync(CancellationToken.None);
        await listener.StopListeningAsync(CancellationToken.None);
        await listener.StartListeningAsync(CancellationToken.None);

        Task stop = listener.StopListeningAsync(CancellationToken.None);
        await stopping.Task.WaitAsync(TestTimeout);
        listener.Dispose();

        second.Verify(l => l.Dispose(), Times.Once);
        third.Verify(l => l.Dispose(), Times.Never);
        _initial.Verify(l => l.Dispose(), Times.Never);
        finishStop.SetResult();
        await Task.WhenAll(stop, initialDisposed.Task, thirdDisposed.Task).WaitAsync(TestTimeout);
        third.Verify(l => l.Dispose(), Times.Once);
        _initial.Verify(l => l.Dispose(), Times.Once);
    }

    [Fact]
    public async Task Dispose_DuringStop_LogsDeferredDisposalFailure()
    {
        TaskCompletionSource stopping = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource finishStop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource logged = new(TaskCreationOptions.RunContinuationsAsynchronously);
        InvalidOperationException failure = new("dispose failed");
        Mock<ILogger> logger = new();
        logger.Setup(l => l.IsEnabled(LogLevel.Error)).Returns(true);
        logger.Setup(l => l.Log(LogLevel.Error, It.Is<EventId>(id => id.Id == 7), It.IsAny<It.IsAnyType>(), failure,
            It.IsAny<Func<It.IsAnyType, Exception, string>>())).Callback(() => logged.TrySetResult());
        _initial.Setup(l => l.StopAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            stopping.TrySetResult();
            return finishStop.Task;
        });
        _initial.Setup(l => l.Dispose()).Throws(failure);
        WorkerGatedListener listener = CreateListener(logger: logger.Object);
        await listener.StartAsync(CancellationToken.None);
        await listener.StartListeningAsync(CancellationToken.None);

        Task stop = listener.StopListeningAsync(CancellationToken.None);
        await stopping.Task.WaitAsync(TestTimeout);
        listener.Dispose();
        finishStop.SetResult();

        await Task.WhenAll(stop, logged.Task).WaitAsync(TestTimeout);
        _initial.Verify(l => l.Dispose(), Times.Once);
    }

    [Fact]
    public async Task Dispose_WithRunningListener_StopsBeforeDisposing()
    {
        TaskCompletionSource stopping = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource finishStop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _initial.Setup(l => l.StopAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            stopping.TrySetResult();
            return finishStop.Task;
        });
        _initial.Setup(l => l.Dispose()).Callback(() => disposed.TrySetResult());
        WorkerGatedListener listener = CreateListener();
        await listener.StartAsync(CancellationToken.None);
        await listener.StartListeningAsync(CancellationToken.None);

        try
        {
            listener.Dispose();

            Assert.True(stopping.Task.IsCompleted);
            Assert.False(disposed.Task.IsCompleted);
            finishStop.TrySetResult();
            await disposed.Task.WaitAsync(TestTimeout);
        }
        finally
        {
            finishStop.TrySetResult();
        }
    }

    [Fact]
    public async Task Dispose_AfterStopFailure_RetainsSingletonLeaseUntilStopCompletes()
    {
        TaskCompletionSource stopping = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource finishStop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        int leaseHeld = 0;
        Mock<IHostIdProvider> hostId = new();
        hostId.Setup(p => p.GetHostIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync("worker-gated-dispose-test");
#pragma warning disable CS0618 // The WebJobs singleton lock interface is marked as a preview API.
        Mock<IDistributedLock> lease = new();
        lease.Setup(l => l.LockId).Returns("timer");
        Mock<IDistributedLockManager> lockManager = new();
        lockManager.Setup(l => l.TryLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.CompareExchange(ref leaseHeld, 1, 0) == 0 ? lease.Object : null);
        lockManager.Setup(l => l.ReleaseLockAsync(It.IsAny<IDistributedLock>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                Interlocked.Exchange(ref leaseHeld, 0);
                released.TrySetResult();
                return Task.CompletedTask;
            });
        using IHost webJobs = new HostBuilder().ConfigureWebJobs()
            .ConfigureServices(services => services.AddSingleton(hostId.Object).AddSingleton(lockManager.Object))
            .Build();
#pragma warning restore CS0618
        IHostSingletonManager singleton = webJobs.Services.GetRequiredService<IHostSingletonManager>();
        Mock<IListener> timer = new();
        timer.Setup(l => l.StopAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                return Task.FromException(new InvalidOperationException("stop failed"));
            }

            stopping.TrySetResult();
            return finishStop.Task;
        });
        WorkerGatedListener listener = new(singleton.CreateHostSingletonListener(timer.Object, "timer"), _factory.Object,
            "Timer", "function:Timer", false, _manager, _timeProvider, NullLogger.Instance, cancelDuringStart: true);
        await listener.StartAsync(CancellationToken.None);
        await listener.StartListeningAsync(CancellationToken.None);
        Assert.False(await listener.StopListeningAsync(CancellationToken.None));

        try
        {
            listener.Dispose();

            Assert.True(stopping.Task.IsCompleted);
            Assert.Equal(1, leaseHeld);
            timer.Verify(l => l.Dispose(), Times.Never);
            finishStop.TrySetResult();
            await released.Task.WaitAsync(TestTimeout);
            Assert.Equal(0, leaseHeld);
        }
        finally
        {
            finishStop.TrySetResult();
        }
    }

    [Fact]
    public async Task Dispose_AfterStopFailure_WhenRetryFails_RetainsGeneration()
    {
        InvalidOperationException failure = new("stop failed");
        _initial.Setup(l => l.StopAsync(It.IsAny<CancellationToken>())).ThrowsAsync(failure);
        WorkerGatedListener listener = CreateListener();
        await listener.StartAsync(CancellationToken.None);
        await listener.StartListeningAsync(CancellationToken.None);
        Assert.False(await listener.StopListeningAsync(CancellationToken.None));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(listener.Dispose);

        Assert.Same(failure, exception);
        _initial.Verify(l => l.StopAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        _initial.Verify(l => l.Dispose(), Times.Never);
    }

    [Fact]
    public async Task Dispose_RetiredDisposeAndCurrentStopFailures_PreservesBoth()
    {
        InvalidOperationException disposeFailure = new("dispose failed");
        InvalidOperationException stopFailure = new("stop failed");
        Mock<IListener> second = new();
        Mock<IListener> third = new();
        second.Setup(l => l.Dispose()).Throws(disposeFailure);
        third.Setup(l => l.StopAsync(It.IsAny<CancellationToken>())).ThrowsAsync(stopFailure);
        _factory.SetupSequence(f => f.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(second.Object)
            .ReturnsAsync(third.Object);
        WorkerGatedListener listener = CreateListener();
        await listener.StartAsync(CancellationToken.None);
        await listener.StartListeningAsync(CancellationToken.None);
        await listener.StopListeningAsync(CancellationToken.None);
        await listener.StartListeningAsync(CancellationToken.None);
        await listener.StopListeningAsync(CancellationToken.None);
        await listener.StartListeningAsync(CancellationToken.None);

        AggregateException exception = Assert.Throws<AggregateException>(listener.Dispose);

        Assert.Contains(disposeFailure, exception.InnerExceptions);
        Assert.Contains(stopFailure, exception.InnerExceptions);
        _initial.Verify(l => l.Dispose(), Times.Once);
        third.Verify(l => l.Dispose(), Times.Never);
    }

    [Fact]
    public async Task Dispose_DisposesEveryGeneration()
    {
        Mock<IListener> second = new();
        Mock<IListener> third = new();
        _factory.SetupSequence(f => f.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(second.Object)
            .ReturnsAsync(third.Object);
        WorkerGatedListener listener = CreateListener();
        await listener.StartAsync(CancellationToken.None);
        await listener.StartListeningAsync(CancellationToken.None);
        await listener.StopListeningAsync(CancellationToken.None);
        await listener.StartListeningAsync(CancellationToken.None);
        await listener.StopListeningAsync(CancellationToken.None);
        await listener.StartListeningAsync(CancellationToken.None);

        listener.Dispose();
        listener.Dispose();

        _initial.Verify(l => l.Dispose(), Times.Once);
        second.Verify(l => l.Dispose(), Times.Once);
        third.Verify(l => l.Dispose(), Times.Once);
        Assert.False(listener.IsListening);
    }

    [Fact]
    public async Task Dispose_ListenerFailure_DisposesRemainingGenerations()
    {
        Mock<IListener> second = new();
        second.Setup(l => l.Dispose()).Throws(new InvalidOperationException("boom"));
        _factory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(second.Object);
        WorkerGatedListener listener = CreateListener();
        await listener.StartAsync(CancellationToken.None);
        await listener.StartListeningAsync(CancellationToken.None);
        await listener.StopListeningAsync(CancellationToken.None);
        await listener.StartListeningAsync(CancellationToken.None);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(listener.Dispose);

        Assert.Equal("boom", exception.Message);
        _initial.Verify(l => l.Dispose(), Times.Once);
    }

    private WorkerGatedListener CreateListener(bool startOnly = false, ILogger logger = null, bool cancelDuringStart = false)
        => new(_initial.Object, _factory.Object, "TestFunction", "function:TestFunction", startOnly, _manager, _timeProvider,
            logger ?? NullLogger.Instance, cancelDuringStart);

    public static class TimerJob
    {
        public static TaskCompletionSource Invocation { get; set; }

        public static Task InvocationCompletion { get; set; }

        public static Task Run([TimerTrigger("0 0 */2 * * *", RunOnStartup = true)] TimerInfo timer)
        {
            Invocation.TrySetResult();
            return InvocationCompletion;
        }
    }
}
