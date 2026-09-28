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

    private readonly Lock _stateLock = new();
    private readonly Mock<IComputeRuntimeStateManager> _stateManager = new(MockBehavior.Strict);
    private readonly Mock<IAppServerHostStateClient> _client = new(MockBehavior.Strict);
    private readonly Mock<IOptionsMonitor<StandbyOptions>> _standbyOptions = new(MockBehavior.Strict);
    private readonly Mock<TimeProvider> _timeProvider = new() { CallBase = true };
    private readonly BlockingCollection<ComputeRuntimeState> _published = new();
    private readonly BlockingCollection<TimeSpan> _timerDelays = new();
    private readonly BlockingCollection<long> _changeWaits = new();
    private readonly TaskCompletionSource _waitingForSpecialization = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentQueue<Func<CancellationToken, Task<HttpStatusCode>>> _responses = new();
    private ComputeRuntimeState _current = new(100, 0, 0);
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
        _stateManager.Setup(manager => manager.WaitForChangeAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns((long lastKnownVersion, CancellationToken cancellationToken) => WaitForChangeAsync(lastKnownVersion, cancellationToken));
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

        // Record each delay the publisher schedules while keeping real timers.
        _timeProvider.Setup(provider => provider.CreateTimer(
                It.IsAny<TimerCallback>(), It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
            .Returns((TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            {
                _timerDelays.Add(dueTime);
                return TimeProvider.System.CreateTimer(callback, state, dueTime, period);
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

        Assert.Equal(new ComputeRuntimeState(100, 0, 0), TakePublished());
        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task ExecuteAsync_PublishesEachNewSnapshot()
    {
        using AppServerHostStatePublisher publisher = await StartPublisherAsync();
        ComputeRuntimeState initial = TakePublished();

        SetState(new(101, 1, 1));
        ComputeRuntimeState changed = TakePublished();
        SetState(new(102, 2, 1));
        ComputeRuntimeState changedAgain = TakePublished();

        Assert.Equal(new ComputeRuntimeState(100, 0, 0), initial);
        Assert.Equal(new ComputeRuntimeState(101, 1, 1), changed);
        Assert.Equal(new ComputeRuntimeState(102, 2, 1), changedAgain);
        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task ExecuteAsync_RetriesLatestSnapshotUntilAppServerAcceptsIt()
    {
        _responses.Enqueue(_ =>
        {
            // A newer snapshot arrives while the rejected one is in flight.
            SetState(new(101, 1, 1));
            return Task.FromResult(HttpStatusCode.InternalServerError);
        });
        _responses.Enqueue(_ => Task.FromResult(HttpStatusCode.Conflict));
        _responses.Enqueue(_ => Task.FromException<HttpStatusCode>(new HttpRequestException("connection refused")));

        // HttpClient reports its request timeout as a TaskCanceledException.
        _responses.Enqueue(_ => Task.FromException<HttpStatusCode>(new TaskCanceledException("request timed out", new TimeoutException())));
        _responses.Enqueue(_ => Task.FromResult(HttpStatusCode.Unauthorized));
        using AppServerHostStatePublisher publisher = await StartPublisherAsync();

        ComputeRuntimeState[] attempts =
            [TakePublished(), TakePublished(), TakePublished(), TakePublished(), TakePublished(), TakePublished()];
        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Equal(new ComputeRuntimeState(100, 0, 0), attempts[0]);
        Assert.All(attempts[1..], attempt => Assert.Equal(new ComputeRuntimeState(101, 1, 1), attempt));

        // The accepted snapshot is neither retried nor published again on shutdown.
        Assert.Empty(_published);
    }

    [Fact]
    public async Task ExecuteAsync_FailedPublish_BacksOffUpToMaxRetryDelayAndResetsAfterSuccess()
    {
        EnqueueStatusCodes(
            HttpStatusCode.InternalServerError,
            HttpStatusCode.InternalServerError,
            HttpStatusCode.InternalServerError,
            HttpStatusCode.InternalServerError,
            HttpStatusCode.InternalServerError,
            HttpStatusCode.InternalServerError,
            HttpStatusCode.OK,
            HttpStatusCode.InternalServerError);
        using AppServerHostStatePublisher publisher = await StartPublisherAsync(maxRetryDelay: TimeSpan.FromMilliseconds(50));
        for (int i = 0; i < 7; i++)
        {
            TakePublished();
        }

        SetState(new(101, 1, 1));

        // Each failure doubles the delay up to the maximum, and the first failure after a success starts over.
        Assert.Equal([5, 10, 20, 40, 50, 50, 5], TakeTimerDelays(7));
        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task ExecuteAsync_PublishFailure_LogsWarningThroughHostLogFilters()
    {
        Mock<ILogger> logger = new();
        using ILoggerFactory loggerFactory = CreateLoggerFactoryWithHostFilters(logger);
        _responses.Enqueue(_ => Task.FromException<HttpStatusCode>(new HttpRequestException("connection refused")));
        using AppServerHostStatePublisher publisher = await StartPublisherAsync(loggerFactory: loggerFactory);

        TakePublished();
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
        WaitUntilPublisherWaitsForChange(100);

        SetState(new(101, 1, 1));
        ComputeRuntimeState next = TakePublished();

        // No delay was scheduled after the accepted publish, so only the newer snapshot could trigger the next one.
        Assert.Equal(new ComputeRuntimeState(101, 1, 1), next);
        Assert.Empty(_timerDelays);
        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task StopAsync_PublishesFinalZeroSnapshotOnce()
    {
        using AppServerHostStatePublisher publisher = await StartPublisherAsync();
        TakePublished();
        SetState(new(101, 1, 1));
        TakePublished();
        WaitUntilPublisherWaitsForChange(101);

        // The state manager publishes zero counts when the application starts stopping.
        SetState(new(102, 0, 0), notify: false);
        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Equal(new ComputeRuntimeState(102, 0, 0), TakePublished());
        Assert.Empty(_published);
    }

    [Fact]
    public async Task StopAsync_DoesNotRepublishAcceptedSnapshot()
    {
        using AppServerHostStatePublisher publisher = await StartPublisherAsync();
        TakePublished();
        WaitUntilPublisherWaitsForChange(100);

        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Empty(_published);
    }

    [Fact]
    public async Task StopAsync_BoundsFinalPublishWhenAppServerDoesNotRespond()
    {
        Mock<ILogger> logger = new();
        using ILoggerFactory loggerFactory = CreateLoggerFactoryWithHostFilters(logger);
        using AppServerHostStatePublisher publisher = await StartPublisherAsync(loggerFactory: loggerFactory);
        TakePublished();
        WaitUntilPublisherWaitsForChange(100);
        _responses.Enqueue(cancellationToken => Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(
            _ => HttpStatusCode.OK, CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default));

        SetState(new(101, 0, 0), notify: false);
        await publisher.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Equal(new ComputeRuntimeState(101, 0, 0), TakePublished());
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
        _stateManager.Setup(manager => manager.WaitForChangeAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
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
        _timerDelays.Dispose();
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

    private async Task<AppServerHostStatePublisher> StartPublisherAsync(TimeSpan? maxRetryDelay = null, ILoggerFactory? loggerFactory = null)
    {
        AppServerHostStatePublisher publisher = new(
            _stateManager.Object,
            _client.Object,
            _standbyOptions.Object,
            _timeProvider.Object,
            loggerFactory ?? NullLoggerFactory.Instance,
            initialRetryDelay: TimeSpan.FromMilliseconds(5),
            maxRetryDelay: maxRetryDelay ?? TimeSpan.FromSeconds(1),
            shutdownPublishTimeout: TimeSpan.FromMilliseconds(100));
        await publisher.StartAsync(CancellationToken.None);

        return publisher;
    }

    private ComputeRuntimeState TakePublished()
    {
        Assert.True(_published.TryTake(out ComputeRuntimeState? state, TestTimeout), "Expected a Host state publish.");
        return state!;
    }

    private void WaitUntilPublisherWaitsForChange(long snapshotVersion)
    {
        while (true)
        {
            Assert.True(_changeWaits.TryTake(out long version, TestTimeout), "Expected the publisher to wait for a state change.");
            if (version == snapshotVersion)
            {
                return;
            }
        }
    }

    private int[] TakeTimerDelays(int count)
    {
        int[] delays = new int[count];
        for (int i = 0; i < count; i++)
        {
            Assert.True(_timerDelays.TryTake(out TimeSpan delay, TestTimeout), "Expected a publisher delay.");
            delays[i] = (int)delay.TotalMilliseconds;
        }

        return delays;
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

    private async Task<ComputeRuntimeState> WaitForChangeAsync(long lastKnownVersion, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task changed;
            lock (_stateLock)
            {
                if (_current.SnapshotVersion > lastKnownVersion)
                {
                    return _current;
                }

                changed = _changed.Task;
            }

            _changeWaits.Add(lastKnownVersion);
            await changed.WaitAsync(cancellationToken);
        }
    }
}
