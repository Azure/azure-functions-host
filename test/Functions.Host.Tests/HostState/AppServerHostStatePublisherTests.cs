// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Azure.Functions.Host.HostState;
using Azure.Functions.Rpc.Client;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Azure.Functions.Host.Tests.HostState;

public sealed class AppServerHostStatePublisherTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);
    private static readonly DateTimeOffset CreatedTime = new(2026, 10, 2, 12, 34, 56, TimeSpan.Zero);

    private readonly Lock _stateLock = new();
    private readonly Mock<IRuntimeStateManager> _stateManager = new(MockBehavior.Strict);
    private readonly RecordingClient _client = new();
    private readonly Mock<IOptionsMonitor<StandbyOptions>> _standbyOptions = new(MockBehavior.Strict);
    private readonly Mock<IDisposable> _standbySubscription = new();
    private readonly Mock<TimeProvider> _timeProvider = new() { CallBase = true };
    private readonly Channel<(RuntimeState State, bool AlreadyPublished)> _samples = Channel.CreateUnbounded<(RuntimeState, bool)>();
    private readonly Channel<ScheduledTimer> _timers = Channel.CreateUnbounded<ScheduledTimer>();
    private readonly TaskCompletionSource _waitingForSpecialization = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private RuntimeState _current = new(CreatedTime, 0, 0, 0);
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private StandbyOptions _standby = new() { InStandbyMode = false };
    private Action<StandbyOptions, string?>? _standbyChanged;
    private Action? _onChangeCaptured;
    private Action? _onCurrentRead;

    public AppServerHostStatePublisherTests()
    {
        _stateManager.SetupGet(manager => manager.Current).Returns(() =>
        {
            RuntimeState state = CurrentState;
            Interlocked.Exchange(ref _onCurrentRead, null)?.Invoke();
            _samples.Writer.TryWrite((state, ReferenceEquals(_client.LastSuccessfulSnapshot, state)));

            return state;
        });
        _stateManager.Setup(manager => manager.GetChangeTask()).Returns(() =>
        {
            Task changed = ChangeTask;
            Interlocked.Exchange(ref _onChangeCaptured, null)?.Invoke();

            return changed;
        });
        _standbyOptions.SetupGet(options => options.CurrentValue).Returns(() =>
        {
            lock (_stateLock)
            {
                return _standby;
            }
        });
        _standbyOptions.Setup(options => options.OnChange(It.IsAny<Action<StandbyOptions, string?>>()))
            .Returns((Action<StandbyOptions, string?> listener) =>
            {
                lock (_stateLock)
                {
                    _standbyChanged = listener;
                }

                _waitingForSpecialization.TrySetResult();
                return _standbySubscription.Object;
            });
        _timeProvider.Setup(provider => provider.CreateTimer(
                It.IsAny<TimerCallback>(), It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
            .Returns((TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            {
                ScheduledTimer timer = new(callback, state, dueTime, period);
                timer.Timer.Setup(instance => instance.Dispose()).Callback(() => timer.Disposed.TrySetResult());
                _timers.Writer.TryWrite(timer);

                return timer.Timer.Object;
            });
    }

    private RuntimeState CurrentState
    {
        get
        {
            lock (_stateLock)
            {
                return _current;
            }
        }
    }

    private Task ChangeTask
    {
        get
        {
            lock (_stateLock)
            {
                return _changed.Task;
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_InStandbyMode_PublishesLatestSnapshotOnlyAfterSpecialization()
    {
        _standby = new() { InStandbyMode = true };
        using AppServerHostStatePublisher publisher = await StartPublisherAsync();
        await _waitingForSpecialization.Task.WaitAsync(TestTimeout);
        AssertNoMorePublications();
        RuntimeState latest = new(CreatedTime, 36, 2, 2);
        SetState(latest);

        Specialize();

        Assert.Same(latest, await TakePublishedAsync());
        await WaitUntilIdleAsync(latest);
        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
        AssertNoMorePublications();
        _standbySubscription.Verify(subscription => subscription.Dispose(), Times.Once());
    }

    [Fact]
    public async Task ExecuteAsync_SpecializationBeforeSubscriptionRecheck_IsNotMissed()
    {
        _standby = new() { InStandbyMode = true };
        _standbyOptions.Setup(options => options.OnChange(It.IsAny<Action<StandbyOptions, string?>>()))
            .Returns((Action<StandbyOptions, string?> _) =>
            {
                Specialize(notify: false);
                return _standbySubscription.Object;
            });
        using AppServerHostStatePublisher publisher = await StartPublisherAsync();

        Assert.Same(CurrentState, await TakePublishedAsync());
        await WaitUntilIdleAsync(CurrentState);
        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task ExecuteAsync_PublishesConfiguredTotalsAndCountOnlyChanges()
    {
        using AppServerHostStatePublisher publisher = await StartPublisherAsync();
        Assert.Equal(new(CreatedTime, 0, 0, 0), await TakePublishedAsync());
        await WaitUntilIdleAsync(CurrentState);
        RuntimeState[] states =
        [
            new(CreatedTime, 4, 1, 1),
            new(CreatedTime, 36, 2, 2),
            new(CreatedTime, 36, 3, 2),
            new(CreatedTime, 36, 3, 3),
        ];

        foreach (RuntimeState state in states)
        {
            SetState(state);
            Assert.Same(state, await TakePublishedAsync());
            await WaitUntilIdleAsync(state);
        }

        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
        AssertNoMorePublications();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteAsync_ChangeBetweenCaptureReadAndSend_IsNotMissedOrDuplicated(bool changeBeforeRead)
    {
        RuntimeState initial = CurrentState;
        RuntimeState latest = new(CreatedTime, 4, 1, 1);
        if (changeBeforeRead)
        {
            _onChangeCaptured = () => SetState(latest);
        }
        else
        {
            _onCurrentRead = () => SetState(latest);
        }

        using AppServerHostStatePublisher publisher = await StartPublisherAsync();
        Assert.Same(changeBeforeRead ? latest : initial, await TakePublishedAsync());
        if (!changeBeforeRead)
        {
            Assert.Same(latest, await TakePublishedAsync());
        }

        await WaitUntilIdleAsync(latest);
        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
        AssertNoMorePublications();
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ExecuteAsync_ChangeDuringPublish_SendsLatestSnapshotNext(HttpStatusCode firstStatus)
    {
        RuntimeState initial = CurrentState;
        RuntimeState latest = new(CreatedTime, 36, 2, 2);
        _client.Enqueue(_ =>
        {
            SetState(latest);
            return Task.FromResult(firstStatus);
        });
        using AppServerHostStatePublisher publisher = await StartPublisherAsync();

        Assert.Same(initial, await TakePublishedAsync());
        Assert.Same(latest, await TakePublishedAsync());
        await WaitUntilIdleAsync(latest);
        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
        AssertNoMorePublications();
        Assert.False(_timers.Reader.TryRead(out _));
    }

    [Fact]
    public async Task ExecuteAsync_AcceptedSnapshot_DoesNotSpinOrScheduleResends()
    {
        using AppServerHostStatePublisher publisher = await StartPublisherAsync();
        Assert.Same(CurrentState, await TakePublishedAsync());
        await WaitUntilIdleAsync(CurrentState);

        await Task.Delay(50);

        _stateManager.Verify(manager => manager.GetChangeTask(), Times.Exactly(2));
        _stateManager.VerifyGet(manager => manager.Current, Times.Exactly(2));
        Assert.False(_timers.Reader.TryRead(out _));
        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
        AssertNoMorePublications();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ExecuteAsync_DistinctSnapshot_IsNotDeduplicatedByValueOrTimestamp(int secondsOffset)
    {
        using AppServerHostStatePublisher publisher = await StartPublisherAsync();
        RuntimeState initial = await TakePublishedAsync();
        await WaitUntilIdleAsync(initial);
        RuntimeState replacement = initial with { CreatedTime = CreatedTime.AddSeconds(secondsOffset) };

        SetState(replacement);

        Assert.Same(replacement, await TakePublishedAsync());
        await WaitUntilIdleAsync(replacement);
        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
        AssertNoMorePublications();
    }

    [Fact]
    public async Task ExecuteAsync_FailedPublish_BacksOffUpToMaximumAndResetsAfterSuccess()
    {
        int[] expectedDelays = [250, 500, 1000, 2000, 4000, 8000, 16000, 30000, 30000];
        for (int i = 0; i < expectedDelays.Length; i++)
        {
            _client.Enqueue(_ => Task.FromResult(HttpStatusCode.InternalServerError));
        }

        using AppServerHostStatePublisher publisher = await StartPublisherAsync();
        foreach (int expectedDelay in expectedDelays)
        {
            Assert.Same(CurrentState, await TakePublishedAsync());
            ScheduledTimer retry = await TakeTimerAsync();
            Assert.Equal(TimeSpan.FromMilliseconds(expectedDelay), retry.DueTime);
            retry.Fire();
        }

        Assert.Same(CurrentState, await TakePublishedAsync());
        await WaitUntilIdleAsync(CurrentState);
        _client.Enqueue(_ => Task.FromResult(HttpStatusCode.InternalServerError));
        SetState(new(CreatedTime, 4, 1, 1));
        Assert.Same(CurrentState, await TakePublishedAsync());
        ScheduledTimer resetRetry = await TakeTimerAsync();
        Assert.Equal(TimeSpan.FromMilliseconds(250), resetRetry.DueTime);
        resetRetry.Fire();
        Assert.Same(CurrentState, await TakePublishedAsync());
        await WaitUntilIdleAsync(CurrentState);
        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ExecuteAsync_NewerSnapshot_InterruptsRetryBackoffRegardlessOfCreatedTime(int secondsOffset)
    {
        _client.Enqueue(_ => Task.FromResult(HttpStatusCode.InternalServerError));
        using AppServerHostStatePublisher publisher = await StartPublisherAsync();
        await TakePublishedAsync();
        ScheduledTimer retry = await TakeTimerAsync();
        Task sharedChange = ChangeTask;
        RuntimeState latest = new(CreatedTime.AddSeconds(secondsOffset), 36, 2, 2);

        SetState(latest);

        Assert.Same(latest, await TakePublishedAsync());
        await retry.Disposed.Task.WaitAsync(TestTimeout);
        await WaitUntilIdleAsync(latest);
        Assert.True(sharedChange.IsCompletedSuccessfully);
        Assert.False(ChangeTask.IsCompleted);
        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
        AssertNoMorePublications();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_RequestFailure_RetriesWithoutCancelingSharedChangeTask(bool requestTimeout)
    {
        Exception exception = requestTimeout
            ? new TaskCanceledException("request timed out", new TimeoutException())
            : new HttpRequestException("connection refused");
        _client.Enqueue(_ => Task.FromException<HttpStatusCode>(exception));
        Mock<ILogger> logger = new();
        using ILoggerFactory loggerFactory = CreateLoggerFactory(logger);
        Task sharedChange = ChangeTask;
        using AppServerHostStatePublisher publisher = await StartPublisherAsync(loggerFactory);
        await TakePublishedAsync();
        ScheduledTimer retry = await TakeTimerAsync();

        retry.Fire();

        Assert.Same(CurrentState, await TakePublishedAsync());
        await WaitUntilIdleAsync(CurrentState);
        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
        Assert.False(sharedChange.IsCompleted);
        VerifyLog(logger, LogLevel.Warning, 1001, exception);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, LogLevel.Information, 1000)]
    [InlineData(HttpStatusCode.Unauthorized, LogLevel.Warning, 1002)]
    [InlineData(HttpStatusCode.Forbidden, LogLevel.Warning, 1002)]
    [InlineData(HttpStatusCode.Conflict, LogLevel.Error, 1003)]
    [InlineData(HttpStatusCode.InternalServerError, LogLevel.Warning, 1002)]
    public async Task ExecuteAsync_PublishResponse_LogsAssignedEventId(HttpStatusCode statusCode, LogLevel level, int expectedEventId)
    {
        Mock<ILogger> logger = new();
        using ILoggerFactory loggerFactory = CreateLoggerFactory(logger);
        _client.Enqueue(_ => Task.FromResult(statusCode));
        using AppServerHostStatePublisher publisher = await StartPublisherAsync(loggerFactory);
        await TakePublishedAsync();
        if (statusCode is HttpStatusCode.OK)
        {
            await WaitUntilIdleAsync(CurrentState);
        }
        else
        {
            await TakeTimerAsync();
        }

        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        VerifyLog(logger, level, expectedEventId);
    }

    [Fact]
    public async Task ExecuteAsync_FaultedChangeTask_IsLoggedWithoutFaultingHost()
    {
        InvalidOperationException failure = new("state tracking failed");
        _stateManager.Setup(manager => manager.GetChangeTask()).Returns(Task.FromException(failure));
        Mock<ILogger> logger = new();
        using ILoggerFactory loggerFactory = CreateLoggerFactory(logger);
        using AppServerHostStatePublisher publisher = await StartPublisherAsync(loggerFactory);
        await TakePublishedAsync();

        await publisher.ExecuteTask!.WaitAsync(TestTimeout);
        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.True(publisher.ExecuteTask.IsCompletedSuccessfully);
        VerifyLog(logger, LogLevel.Error, 1005, failure);
        AssertNoMorePublications();
    }

    [Theory]
    [InlineData("zero")]
    [InlineData("count")]
    [InlineData("equal")]
    public async Task StopAsync_PublishesDistinctFinalSnapshotOnce(string change)
    {
        SetState(new(CreatedTime, 4, 1, 1));
        using AppServerHostStatePublisher publisher = await StartPublisherAsync();
        RuntimeState initial = await TakePublishedAsync();
        await WaitUntilIdleAsync(initial);
        RuntimeState final = change switch
        {
            "zero" => initial with { CreatedTime = CreatedTime.AddSeconds(-1), HttpCapacity = 0 },
            "count" => initial with { WorkerCount = 2 },
            _ => initial with { },
        };
        SetState(final, notify: false);

        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Same(final, await TakePublishedAsync());
        AssertNoMorePublications();
    }

    [Fact]
    public async Task StopAsync_DoesNotRepublishAcceptedSnapshotOrWithdrawBorrowedState()
    {
        SetState(new(CreatedTime, 4, 1, 1));
        Task sharedChange = ChangeTask;
        using AppServerHostStatePublisher publisher = await StartPublisherAsync();
        RuntimeState published = await TakePublishedAsync();
        await WaitUntilIdleAsync(published);

        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Same(published, CurrentState);
        Assert.False(sharedChange.IsCompleted);
        AssertNoMorePublications();
    }

    [Fact]
    public async Task StopAsync_CancelsInFlightRequestBeforeIndependentFinalRequest()
    {
        SetState(new(CreatedTime, 4, 1, 1));
        TaskCompletionSource<CancellationToken> regularRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource regularCanceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<CancellationToken> finalRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _client.Enqueue(async cancellationToken =>
        {
            regularRequest.TrySetResult(cancellationToken);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                regularCanceled.TrySetResult();
                throw;
            }

            return HttpStatusCode.OK;
        });
        _client.Enqueue(cancellationToken =>
        {
            Assert.True(regularCanceled.Task.IsCompletedSuccessfully);
            finalRequest.TrySetResult(cancellationToken);
            return Task.FromResult(HttpStatusCode.OK);
        });
        using AppServerHostStatePublisher publisher = await StartPublisherAsync();
        await TakePublishedAsync();
        CancellationToken regularToken = await regularRequest.Task.WaitAsync(TestTimeout);
        RuntimeState final = new(CreatedTime, 0, 1, 1);
        SetState(final, notify: false);

        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.True(regularToken.IsCancellationRequested);
        Assert.False((await finalRequest.Task.WaitAsync(TestTimeout)).IsCancellationRequested);
        Assert.Same(final, await TakePublishedAsync());
        Assert.True(publisher.ExecuteTask!.IsCompletedSuccessfully);
        AssertNoMorePublications();
    }

    [Fact]
    public async Task StopAsync_BoundsFinalRequestToTwoSeconds()
    {
        Mock<ILogger> logger = new();
        using ILoggerFactory loggerFactory = CreateLoggerFactory(logger);
        using AppServerHostStatePublisher publisher = await StartPublisherAsync(loggerFactory);
        await TakePublishedAsync();
        await WaitUntilIdleAsync(CurrentState);
        _client.Enqueue(async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return HttpStatusCode.OK;
        });
        RuntimeState final = new(CreatedTime, 0, 1, 1);
        SetState(final, notify: false);

        Task stopping = publisher.StopAsync(CancellationToken.None);
        Assert.Same(final, await TakePublishedAsync());
        ScheduledTimer timeout = await TakeTimerAsync();
        Assert.Equal(TimeSpan.FromSeconds(2), timeout.DueTime);
        Assert.False(stopping.IsCompleted);
        timeout.Fire();
        await stopping.WaitAsync(TestTimeout);

        Assert.True(publisher.ExecuteTask!.IsCompletedSuccessfully);
        VerifyLog(logger, LogLevel.Warning, 1004);
        AssertNoMorePublications();
    }

    [Fact]
    public async Task StopAsync_InStandbyMode_DoesNotPublish()
    {
        _standby = new() { InStandbyMode = true };
        using AppServerHostStatePublisher publisher = await StartPublisherAsync();
        await _waitingForSpecialization.Task.WaitAsync(TestTimeout);

        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.True(publisher.ExecuteTask!.IsCompletedSuccessfully);
        AssertNoMorePublications();
        _stateManager.Verify(manager => manager.GetChangeTask(), Times.Never());
        _standbySubscription.Verify(subscription => subscription.Dispose(), Times.Once());
    }

    [Fact]
    public async Task StopAsync_FailedFinalStateRead_IsLoggedWithoutFaultingHost()
    {
        Mock<ILogger> logger = new();
        using ILoggerFactory loggerFactory = CreateLoggerFactory(logger);
        using AppServerHostStatePublisher publisher = await StartPublisherAsync(loggerFactory);
        await TakePublishedAsync();
        await WaitUntilIdleAsync(CurrentState);
        InvalidOperationException failure = new("final state unavailable");
        _stateManager.SetupGet(manager => manager.Current).Throws(failure);

        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.True(publisher.ExecuteTask!.IsCompletedSuccessfully);
        VerifyLog(logger, LogLevel.Error, 1005, failure);
        AssertNoMorePublications();
    }

    private static ILoggerFactory CreateLoggerFactory(Mock<ILogger> logger)
    {
        logger.Setup(value => value.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        Mock<ILoggerProvider> provider = new();
        provider.Setup(value => value.CreateLogger(It.IsAny<string>())).Returns(logger.Object);

        return LoggerFactory.Create(builder => builder.AddDefaultWebJobsFilters().AddProvider(provider.Object));
    }

    private static void VerifyLog(Mock<ILogger> logger, LogLevel level, int eventId, Exception? exception = null)
    {
        logger.Verify(value => value.Log(
            level, It.Is<EventId>(value => value.Id == eventId), It.IsAny<It.IsAnyType>(), exception,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once());
    }

    private async Task<AppServerHostStatePublisher> StartPublisherAsync(ILoggerFactory? loggerFactory = null)
    {
        AppServerHostStatePublisher publisher = new(
            _stateManager.Object, _client, _standbyOptions.Object, _timeProvider.Object, loggerFactory ?? NullLoggerFactory.Instance);
        await publisher.StartAsync(CancellationToken.None);

        return publisher;
    }

    private async Task<RuntimeState> TakePublishedAsync()
        => await _client.Publications.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout);

    private async Task<ScheduledTimer> TakeTimerAsync()
    {
        ScheduledTimer timer = await _timers.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout);
        Assert.Equal(Timeout.InfiniteTimeSpan, timer.Period);

        return timer;
    }

    private async Task WaitUntilIdleAsync(RuntimeState state)
    {
        while (true)
        {
            (RuntimeState sampled, bool alreadyPublished) = await _samples.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout);
            if (ReferenceEquals(sampled, state) && alreadyPublished)
            {
                return;
            }
        }
    }

    private void AssertNoMorePublications() => Assert.False(_client.Publications.Reader.TryRead(out _));

    private void SetState(RuntimeState state, bool notify = true)
    {
        TaskCompletionSource? changed = null;
        lock (_stateLock)
        {
            _current = state;
            if (notify)
            {
                changed = _changed;
                _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        changed?.TrySetResult();
    }

    private void Specialize(bool notify = true)
    {
        StandbyOptions specialized = new() { InStandbyMode = false };
        Action<StandbyOptions, string?>? listener;
        lock (_stateLock)
        {
            _standby = specialized;
            listener = _standbyChanged;
        }

        if (notify)
        {
            listener?.Invoke(specialized, null);
        }
    }

    private sealed class RecordingClient : IAppServerHostStateClient
    {
        private readonly Lock _lock = new();
        private readonly Queue<Func<CancellationToken, Task<HttpStatusCode>>> _responses = new();
        private RuntimeState? _lastSuccessfulSnapshot;

        public Channel<RuntimeState> Publications { get; } = Channel.CreateUnbounded<RuntimeState>();

        public RuntimeState? LastSuccessfulSnapshot
        {
            get
            {
                lock (_lock)
                {
                    return _lastSuccessfulSnapshot;
                }
            }
        }

        public void Enqueue(Func<CancellationToken, Task<HttpStatusCode>> response)
        {
            lock (_lock)
            {
                _responses.Enqueue(response);
            }
        }

        public async Task<HttpStatusCode> PublishAsync(RuntimeState state, CancellationToken cancellationToken)
        {
            Func<CancellationToken, Task<HttpStatusCode>>? response;
            lock (_lock)
            {
                _responses.TryDequeue(out response);
            }

            Publications.Writer.TryWrite(state);
            HttpStatusCode status = response is null ? HttpStatusCode.OK : await response(cancellationToken);
            if ((int)status is >= 200 and < 300)
            {
                lock (_lock)
                {
                    _lastSuccessfulSnapshot = state;
                }
            }

            return status;
        }
    }

    private sealed record ScheduledTimer(TimerCallback Callback, object? State, TimeSpan DueTime, TimeSpan Period)
    {
        public Mock<ITimer> Timer { get; } = new();

        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Fire() => Callback(State);
    }
}
