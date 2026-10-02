// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
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

public sealed class AppServerHostStatePublisherTests : IDisposable
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);
    private static readonly DateTimeOffset CreatedTime = new(2026, 10, 2, 12, 34, 56, TimeSpan.Zero);

    private readonly Lock _stateLock = new();
    private readonly Mock<IComputeRuntimeStateManager> _stateManager = new(MockBehavior.Strict);
    private readonly Mock<IAppServerHostStateClient> _client = new(MockBehavior.Strict);
    private readonly Mock<IOptionsMonitor<StandbyOptions>> _standbyOptions = new(MockBehavior.Strict);
    private readonly Mock<TimeProvider> _timeProvider = new() { CallBase = true };
    private readonly BlockingCollection<ComputeRuntimeState> _published = new();
    private readonly BlockingCollection<ScheduledTimer> _timers = new();
    private readonly BlockingCollection<ComputeRuntimeState> _changeWaits = new();
    private readonly TaskCompletionSource _waitingForSpecialization = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentQueue<Func<CancellationToken, Task<HttpStatusCode>>> _responses = new();
    private ComputeRuntimeState _current = new(CreatedTime, 0, 0, 0);
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private StandbyOptions _standby = new() { InStandbyMode = false };
    private Action<StandbyOptions, string?>? _standbyChanged;

    public AppServerHostStatePublisherTests()
    {
        _stateManager.SetupGet(manager => manager.Current)
            .Returns(() =>
            {
                lock (_stateLock)
                {
                    return _current;
                }
            });
        _stateManager.Setup(manager => manager.WaitForChangeAsync(It.IsAny<ComputeRuntimeState>(), It.IsAny<CancellationToken>()))
            .Returns((ComputeRuntimeState lastKnownState, CancellationToken cancellationToken) => WaitForChangeAsync(lastKnownState, cancellationToken));
        _client.Setup(client => client.PublishAsync(It.IsAny<ComputeRuntimeState>(), It.IsAny<CancellationToken>()))
            .Returns((ComputeRuntimeState state, CancellationToken cancellationToken) =>
            {
                // Pick the response before the publish is observable so a response enqueued afterward targets a later publish.
                _responses.TryDequeue(out Func<CancellationToken, Task<HttpStatusCode>>? response);
                _published.Add(state);
                return response?.Invoke(cancellationToken) ?? Task.FromResult(HttpStatusCode.OK);
            });
        _standbyOptions.SetupGet(options => options.CurrentValue)
            .Returns(() => Volatile.Read(ref _standby));
        _standbyOptions.Setup(options => options.OnChange(It.IsAny<Action<StandbyOptions, string?>>()))
            .Returns((Action<StandbyOptions, string?> listener) =>
            {
                _standbyChanged = listener;
                _waitingForSpecialization.TrySetResult();
                return Mock.Of<IDisposable>();
            });

        _timeProvider.Setup(provider => provider.CreateTimer(
                It.IsAny<TimerCallback>(), It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
            .Returns((TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            {
                ScheduledTimer timer = new(callback, state, dueTime, period);
                timer.Timer.Setup(instance => instance.Dispose()).Callback(() => timer.Disposed.TrySetResult());
                _timers.Add(timer);
                return timer.Timer.Object;
            });
    }

    [Fact]
    public async Task ExecuteAsync_InStandbyMode_PublishesCurrentSnapshotOnlyAfterSpecialization()
    {
        _standby = new() { InStandbyMode = true };
        using AppServerHostStatePublisher publisher = await StartPublisherAsync();
        await _waitingForSpecialization.Task.WaitAsync(TestTimeout);

        Assert.Empty(_published);
        Specialize();

        Assert.Same(_current, TakePublished());
        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task ExecuteAsync_PublishesEachNewSnapshot()
    {
        using AppServerHostStatePublisher publisher = await StartPublisherAsync();
        ComputeRuntimeState initial = TakePublished();

        SetState(new(CreatedTime, 16, 1, 1));
        ComputeRuntimeState changed = TakePublished();
        SetState(new(CreatedTime, 48, 3, 3));
        ComputeRuntimeState changedAgain = TakePublished();

        Assert.Equal(new ComputeRuntimeState(CreatedTime, 0, 0, 0), initial);
        Assert.Equal(new ComputeRuntimeState(CreatedTime, 16, 1, 1), changed);
        Assert.Equal(new ComputeRuntimeState(CreatedTime, 48, 3, 3), changedAgain);
        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ExecuteAsync_CountOnlySnapshots_PublishWithSameCreatedTime(HttpStatusCode initialStatus)
    {
        _current = new(CreatedTime, 16, 1, 1);
        EnqueueStatusCodes(initialStatus);
        using AppServerHostStatePublisher publisher = await StartPublisherAsync();
        Assert.Same(_current, TakePublished());
        WaitUntilPublisherWaitsForChange(_current);

        SetState(new(CreatedTime, 16, 2, 1));
        Assert.Same(_current, TakePublished());
        if (initialStatus is HttpStatusCode.InternalServerError)
        {
            await TakeTimer().Disposed.Task.WaitAsync(TestTimeout);
        }

        WaitUntilPublisherWaitsForChange(_current);
        SetState(new(CreatedTime, 16, 2, 2));
        Assert.Same(_current, TakePublished());
        WaitUntilPublisherWaitsForChange(_current);

        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Empty(_published);
        Assert.Empty(_timers);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ExecuteAsync_EqualValuedDistinctSnapshot_IsNotSkipped(HttpStatusCode initialStatus)
    {
        EnqueueStatusCodes(initialStatus);
        using AppServerHostStatePublisher publisher = await StartPublisherAsync();
        ComputeRuntimeState initial = TakePublished();
        WaitUntilPublisherWaitsForChange(initial);

        ComputeRuntimeState replacement = initial with { };
        Assert.Equal(initial, replacement);
        Assert.NotSame(initial, replacement);
        SetState(replacement);
        Assert.Same(replacement, TakePublished());
        if (initialStatus is HttpStatusCode.InternalServerError)
        {
            await TakeTimer().Disposed.Task.WaitAsync(TestTimeout);
        }

        WaitUntilPublisherWaitsForChange(replacement);
        SetState(new(CreatedTime, 16, 1, 1));
        ComputeRuntimeState changed = TakePublished();
        WaitUntilPublisherWaitsForChange(changed);
        SetState(initial with { });
        Assert.Same(_current, TakePublished());
        Assert.Equal(initial, _current);
        WaitUntilPublisherWaitsForChange(_current);

        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Empty(_published);
        Assert.Empty(_timers);
    }

    [Fact]
    public async Task ExecuteAsync_RetriesLatestSnapshotUntilAppServerAcceptsIt()
    {
        _responses.Enqueue(_ =>
        {
            // A newer snapshot arrives while the rejected one is in flight.
            SetState(new(CreatedTime, 16, 1, 1));
            return Task.FromResult(HttpStatusCode.InternalServerError);
        });
        _responses.Enqueue(_ => Task.FromResult(HttpStatusCode.Conflict));
        _responses.Enqueue(_ => Task.FromException<HttpStatusCode>(new HttpRequestException("connection refused")));

        // HttpClient reports its request timeout as a TaskCanceledException.
        _responses.Enqueue(_ => Task.FromException<HttpStatusCode>(new TaskCanceledException("request timed out", new TimeoutException())));
        _responses.Enqueue(_ => Task.FromResult(HttpStatusCode.Unauthorized));
        using AppServerHostStatePublisher publisher = await StartPublisherAsync();

        ComputeRuntimeState[] attempts = new ComputeRuntimeState[6];
        for (int i = 0; i < attempts.Length - 1; i++)
        {
            attempts[i] = TakePublished();
            ScheduledTimer retry = TakeTimer();
            if (i == 0)
            {
                // The newer snapshot skips the first retry delay without firing its timer.
                await retry.Disposed.Task.WaitAsync(TestTimeout);
            }
            else
            {
                retry.Fire();
            }
        }

        attempts[^1] = TakePublished();
        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Equal(new ComputeRuntimeState(CreatedTime, 0, 0, 0), attempts[0]);
        Assert.All(attempts[1..], attempt =>
        {
            Assert.Same(_current, attempt);
            Assert.Equal(CreatedTime, attempt.CreatedTime);
        });

        // The accepted snapshot is neither retried nor published again on shutdown.
        Assert.Empty(_published);
    }

    [Fact]
    public async Task ExecuteAsync_FailedPublish_BacksOffUpToMaxRetryDelayAndResetsAfterSuccess()
    {
        int[] expectedDelays = [250, 500, 1000, 2000, 4000, 8000, 16000, 30000, 30000];
        for (int i = 0; i < expectedDelays.Length; i++)
        {
            EnqueueStatusCodes(HttpStatusCode.InternalServerError);
        }

        EnqueueStatusCodes(HttpStatusCode.OK, HttpStatusCode.InternalServerError);
        using AppServerHostStatePublisher publisher = await StartPublisherAsync();
        foreach (int expectedDelay in expectedDelays)
        {
            TakePublished();
            ScheduledTimer retry = TakeTimer();
            Assert.Equal(TimeSpan.FromMilliseconds(expectedDelay), retry.DueTime);
            retry.Fire();
        }

        TakePublished();
        WaitUntilPublisherWaitsForChange(_current);
        SetState(new(CreatedTime, 16, 1, 1));

        TakePublished();
        ScheduledTimer resetRetry = TakeTimer();
        Assert.Equal(TimeSpan.FromMilliseconds(250), resetRetry.DueTime);
        resetRetry.Fire();
        TakePublished();
        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task ExecuteAsync_NewerSnapshot_InterruptsRetryBackoffRegardlessOfCreatedTime(int secondsOffset)
    {
        EnqueueStatusCodes(HttpStatusCode.InternalServerError);
        using AppServerHostStatePublisher publisher = await StartPublisherAsync();
        TakePublished();
        ScheduledTimer retry = TakeTimer();
        WaitUntilPublisherWaitsForChange(_current);

        SetState(new(CreatedTime.AddSeconds(secondsOffset), 16, 1, 1));

        // The new snapshot wakes the publisher while the retry timer remains unexpired.
        Assert.Same(_current, TakePublished());
        await retry.Disposed.Task.WaitAsync(TestTimeout);
        Assert.Empty(_timers);
        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
        Assert.Empty(_published);
    }

    [Fact]
    public async Task ExecuteAsync_PublishFailure_LogsWarningThroughHostLogFilters()
    {
        Mock<ILogger> logger = new();
        using ILoggerFactory loggerFactory = CreateLoggerFactoryWithHostFilters(logger);
        _responses.Enqueue(_ => Task.FromException<HttpStatusCode>(new HttpRequestException("connection refused")));
        using AppServerHostStatePublisher publisher = await StartPublisherAsync(loggerFactory: loggerFactory);

        TakePublished();
        TakeTimer().Fire();
        TakePublished();
        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        logger.Verify(value => value.Log(
            LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.Is<Exception?>(exception => exception is HttpRequestException),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once());
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotResendAcceptedSnapshot()
    {
        using AppServerHostStatePublisher publisher = await StartPublisherAsync();
        TakePublished();
        WaitUntilPublisherWaitsForChange(_current);

        SetState(new(CreatedTime, 16, 1, 1));
        ComputeRuntimeState next = TakePublished();

        // No delay was scheduled after the accepted publish, so only the newer snapshot could trigger the next one.
        Assert.Same(_current, next);
        Assert.Empty(_timers);
        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task StopAsync_PublishesFinalZeroSnapshotOnceRegardlessOfCreatedTime(int secondsOffset)
    {
        using AppServerHostStatePublisher publisher = await StartPublisherAsync();
        TakePublished();
        SetState(new(CreatedTime, 16, 1, 1));
        TakePublished();
        WaitUntilPublisherWaitsForChange(_current);

        // The state manager withdraws capacity when the application starts stopping.
        SetState(new(CreatedTime.AddSeconds(secondsOffset), 0, 1, 1), notify: false);
        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Same(_current, TakePublished());
        Assert.Empty(_published);
    }

    [Fact]
    public async Task StopAsync_PublishesFinalCountOnlySnapshotOnce()
    {
        using AppServerHostStatePublisher publisher = await StartPublisherAsync();
        Assert.Same(_current, TakePublished());
        WaitUntilPublisherWaitsForChange(_current);

        SetState(new(CreatedTime, 0, 1, 1), notify: false);
        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Same(_current, TakePublished());
        Assert.Empty(_published);
    }

    [Fact]
    public async Task StopAsync_PublishesEqualValuedDistinctFinalSnapshot()
    {
        using AppServerHostStatePublisher publisher = await StartPublisherAsync();
        ComputeRuntimeState initial = TakePublished();
        WaitUntilPublisherWaitsForChange(initial);
        ComputeRuntimeState final = initial with { };

        SetState(final, notify: false);
        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Equal(initial, final);
        Assert.NotSame(initial, final);
        Assert.Same(final, TakePublished());
        Assert.Empty(_published);
    }

    [Fact]
    public async Task StopAsync_DoesNotRepublishAcceptedSnapshot()
    {
        using AppServerHostStatePublisher publisher = await StartPublisherAsync();
        TakePublished();
        WaitUntilPublisherWaitsForChange(_current);

        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Empty(_published);
    }

    [Fact]
    public async Task StopAsync_CancelsInFlightPublishBeforeSendingFinalZeroSnapshot()
    {
        TaskCompletionSource<CancellationToken> regularRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<CancellationToken> finalRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource regularRequestCanceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool canceledBeforeFinalPublish = false;
        _current = new(CreatedTime, 16, 1, 1);
        _responses.Enqueue(async cancellationToken =>
        {
            regularRequest.SetResult(cancellationToken);
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                regularRequestCanceled.SetResult();
                throw;
            }

            return HttpStatusCode.OK;
        });
        _responses.Enqueue(cancellationToken =>
        {
            canceledBeforeFinalPublish = regularRequestCanceled.Task.IsCompletedSuccessfully;
            finalRequest.SetResult(cancellationToken);
            return Task.FromResult(HttpStatusCode.OK);
        });
        using AppServerHostStatePublisher publisher = await StartPublisherAsync();
        Assert.Same(_current, TakePublished());
        CancellationToken regularToken = await regularRequest.Task.WaitAsync(TestTimeout);

        SetState(new(CreatedTime, 0, 1, 1), notify: false);
        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.True(regularToken.IsCancellationRequested);
        Assert.True(canceledBeforeFinalPublish);
        Assert.False((await finalRequest.Task.WaitAsync(TestTimeout)).IsCancellationRequested);
        Assert.Same(_current, TakePublished());
        Assert.Empty(_published);
        Assert.True(publisher.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task StopAsync_BoundsFinalPublishWhenAppServerDoesNotRespond()
    {
        Mock<ILogger> logger = new();
        using ILoggerFactory loggerFactory = CreateLoggerFactoryWithHostFilters(logger);
        using AppServerHostStatePublisher publisher = await StartPublisherAsync(loggerFactory: loggerFactory);
        TakePublished();
        WaitUntilPublisherWaitsForChange(_current);
        _responses.Enqueue(cancellationToken => Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(
            _ => HttpStatusCode.OK, CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default));

        SetState(new(CreatedTime, 0, 1, 1), notify: false);
        Task stopping = publisher.StopAsync(CancellationToken.None);
        Assert.Same(_current, TakePublished());
        ScheduledTimer timeout = TakeTimer();
        Assert.Equal(TimeSpan.FromSeconds(2), timeout.DueTime);
        Assert.False(stopping.IsCompleted);
        timeout.Fire();
        await stopping.WaitAsync(TestTimeout);

        Assert.Empty(_published);
        Assert.True(publisher.ExecuteTask!.IsCompletedSuccessfully);

        // The only warning is the final-publish timeout.
        logger.Verify(value => value.Log(
            LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once());
    }

    [Fact]
    public async Task StopAsync_InStandbyMode_DoesNotPublish()
    {
        _standby = new() { InStandbyMode = true };
        using AppServerHostStatePublisher publisher = await StartPublisherAsync();
        await _waitingForSpecialization.Task.WaitAsync(TestTimeout);

        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Empty(_published);
        Assert.True(publisher.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ExecuteAsync_UnexpectedFailure_DoesNotFaultHost()
    {
        _stateManager.Setup(manager => manager.WaitForChangeAsync(It.IsAny<ComputeRuntimeState>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("state manager failed"));
        using AppServerHostStatePublisher publisher = await StartPublisherAsync();
        TakePublished();

        await publisher.ExecuteTask!.WaitAsync(TestTimeout);
        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.True(publisher.ExecuteTask.IsCompletedSuccessfully);
    }

    public void Dispose()
    {
        _published.Dispose();
        _timers.Dispose();
        _changeWaits.Dispose();
    }

    private static ILoggerFactory CreateLoggerFactoryWithHostFilters(Mock<ILogger> logger)
    {
        logger.Setup(value => value.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        Mock<ILoggerProvider> provider = new();
        provider.Setup(value => value.CreateLogger(It.IsAny<string>())).Returns(logger.Object);

        // Apply the Host's root log filters, which drop categories without a system prefix.
        return LoggerFactory.Create(builder => builder.AddDefaultWebJobsFilters().AddProvider(provider.Object));
    }

    private async Task<AppServerHostStatePublisher> StartPublisherAsync(ILoggerFactory? loggerFactory = null)
    {
        AppServerHostStatePublisher publisher = new(
            _stateManager.Object,
            _client.Object,
            _standbyOptions.Object,
            _timeProvider.Object,
            loggerFactory ?? NullLoggerFactory.Instance);
        await publisher.StartAsync(CancellationToken.None);

        return publisher;
    }

    private ComputeRuntimeState TakePublished()
    {
        Assert.True(_published.TryTake(out ComputeRuntimeState? state, TestTimeout), "Expected a Host state publish.");
        return state!;
    }

    private void WaitUntilPublisherWaitsForChange(ComputeRuntimeState state)
    {
        while (true)
        {
            Assert.True(_changeWaits.TryTake(out ComputeRuntimeState? waitingState, TestTimeout), "Expected the publisher to wait for a state change.");
            if (ReferenceEquals(waitingState, state))
            {
                return;
            }
        }
    }

    private ScheduledTimer TakeTimer()
    {
        Assert.True(_timers.TryTake(out ScheduledTimer? timer, TestTimeout), "Expected a publisher timer.");
        Assert.Equal(Timeout.InfiniteTimeSpan, timer!.Period);

        return timer;
    }

    private void EnqueueStatusCodes(params HttpStatusCode[] statusCodes)
    {
        foreach (HttpStatusCode statusCode in statusCodes)
        {
            _responses.Enqueue(_ => Task.FromResult(statusCode));
        }
    }

    private void Specialize()
    {
        StandbyOptions specialized = new() { InStandbyMode = false };
        Volatile.Write(ref _standby, specialized);
        _standbyChanged?.Invoke(specialized, null);
    }

    private void SetState(ComputeRuntimeState state, bool notify = true)
    {
        TaskCompletionSource changed;
        lock (_stateLock)
        {
            _current = state;
            if (!notify)
            {
                return;
            }

            changed = _changed;
            _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        changed.TrySetResult();
    }

    private async Task<ComputeRuntimeState> WaitForChangeAsync(ComputeRuntimeState lastKnownState, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task changed;
            lock (_stateLock)
            {
                if (!ReferenceEquals(_current, lastKnownState))
                {
                    return _current;
                }

                changed = _changed.Task;
            }

            _changeWaits.Add(lastKnownState);
            await changed.WaitAsync(cancellationToken);
        }
    }

    private sealed record ScheduledTimer(TimerCallback Callback, object? State, TimeSpan DueTime, TimeSpan Period)
    {
        public Mock<ITimer> Timer { get; } = new();

        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Fire() => Callback(State);
    }
}
