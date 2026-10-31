// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using GrpcRpcException = Grpc.Core.RpcException;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    [Fact]
    public async Task NeverLinkedDeadline_IncludesConstructionTimeAndUsesMonotonicClock()
    {
        LifecycleClock clock = new();
        DateTimeOffset processStart = clock.GetUtcNow();
        await using WorkerProxyWebApplicationFactory factory = CreateTimeoutFactory(clock, "00:02:00.0000001");
        clock.Advance(TimeSpan.FromSeconds(90));
        clock.AdjustUtc(TimeSpan.FromDays(1));
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpClient management = factory.CreateWorkerProxyClient();

        SplitWorkerOriginState initial = await ReadSplitManagementStateAsync(management, timeout.Token);

        Assert.Equal(processStart, initial.StartupTime);
        Assert.Equal(SplitWorkerStatus.Starting, initial.Status);
        await clock.WaitForTimerAsync(TimeSpan.FromSeconds(30) + TimeSpan.FromMilliseconds(1), timeout.Token);
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(initial, await ReadSplitManagementStateAsync(management, timeout.Token));

        clock.Advance(TimeSpan.FromMilliseconds(1));
        SplitWorkerOriginState removed = await ReadSplitManagementStateAsync(management, timeout.Token);

        Assert.Equal(SplitWorkerStatus.RemovalRequested, removed.Status);
        Assert.Equal(processStart, removed.StartupTime);
        Assert.Equal(initial.RevisionId + 1, removed.RevisionId);
    }

    [Fact]
    public async Task NeverLinkedDeadline_WorkerActivityAssignmentAndReloadDoNotCompleteRuntimeLink()
    {
        LifecycleClock clock = new();
        await using WorkerProxyWebApplicationFactory factory = CreateTimeoutFactory(clock, "00:02:00");
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpClient management = factory.CreateWorkerProxyClient();
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await clock.WaitForTimerAsync(TimeSpan.FromMinutes(2), timeout.Token);
        clock.Advance(TimeSpan.FromMinutes(1));
        await worker.WriteAsync(CreateStartStream(), timeout.Token);
        await WaitForWorkerReadyAsync(manager, timeout.Token);
        using (HttpResponseMessage assignment = await PutManagementJsonAsync(
            management,
            "/admin/worker/assignment",
            ManagementAssignment,
            timeout.Token))
        {
            Assert.Equal(HttpStatusCode.Created, assignment.StatusCode);
        }

        IConfiguration configuration = factory.Services.GetRequiredService<IConfiguration>();
        configuration["WorkerProxy:NeverLinkedTimeout"] = "00:30:00";
        ((IConfigurationRoot)configuration).Reload();
        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(
            SplitWorkerStatus.RemovalRequested,
            (await ReadSplitManagementStateAsync(management, timeout.Token)).Status);
        await AssertReadinessAsync(management, "/admin/worker/ready", HttpStatusCode.ServiceUnavailable, timeout.Token);
        using HttpResponseMessage replay = await PutManagementJsonAsync(
            management,
            "/admin/worker/assignment",
            ManagementAssignment,
            timeout.Token);
        Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
    }

    [Fact]
    public async Task NeverLinkedDeadline_PublishesStickyRemovalAndRejectsLateRuntimeAndWorkerLinks()
    {
        LifecycleClock clock = new();
        using BlockingLogger<FunctionRpcRelay> logger = new(eventIdToBlock: SessionTerminatedEventId);
        await using WorkerProxyWebApplicationFactory factory = CreateTimeoutFactory(
            clock,
            configureServices: services =>
                services.AddSingleton<Microsoft.Extensions.Logging.ILogger<FunctionRpcRelay>>(logger));
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpClient management = factory.CreateWorkerProxyClient();
        await using (RelayClient terminatingWorker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token))
        {
            await WaitForAttachmentAsync(
                factory.Services.GetRequiredService<FunctionRpcRelay>(),
                FunctionRpcRelaySide.Worker,
                timeout.Token);
            await terminatingWorker.CompleteRequestAsync(timeout.Token);
            try
            {
                await logger.LogEntered.WaitAsync(timeout.Token);
                await using RelayClient rejectedRuntime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
                GrpcRpcException rejection = await rejectedRuntime.WriteAndReadRejectionAsync(
                    CreateMessage("runtime-during-teardown"),
                    timeout.Token);
                Assert.Equal(StatusCode.Unavailable, rejection.StatusCode);
            }
            finally
            {
                logger.Release();
            }

            Assert.Equal(StatusCode.Unavailable, await terminatingWorker.WaitForTerminationAsync(timeout.Token));
        }

        SplitWorkerOriginState initial = await ReadSplitManagementStateAsync(management, timeout.Token);
        Task<HttpResponseMessage> poll = PollSplitManagementStateAsync(management, initial, timeout.Token);
        await WaitForManagementPollAsync(manager, timeout.Token);
        await clock.WaitForTimerAsync(TimeSpan.FromMinutes(5), timeout.Token);

        clock.Advance(TimeSpan.FromMinutes(5));
        using HttpResponseMessage changed = await poll;
        SplitWorkerOriginState removed = await ReadSplitManagementStateAsync(changed, timeout.Token);

        Assert.Equal(SplitWorkerStatus.RemovalRequested, removed.Status);
        Assert.Equal(SplitWorkerLinkState.Unlinked, removed.Link.State);
        await using RelayClient lateRuntime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        GrpcRpcException runtimeRejection = await lateRuntime.WriteAndReadRejectionAsync(
            CreateMessage("late-runtime"),
            timeout.Token);
        Assert.Equal(StatusCode.Unavailable, runtimeRejection.StatusCode);

        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        GrpcRpcException workerRejection = await worker.WriteAndReadRejectionAsync(
            CreateStartStream(),
            timeout.Token);
        Assert.Equal(StatusCode.Unavailable, workerRejection.StatusCode);

        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(
            SplitWorkerStatus.RemovalRequested,
            (await ReadSplitManagementStateAsync(management, timeout.Token)).Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NeverLinkedDeadline_BoundarySelectsOneStickyRuntimeLinkOutcome(bool runtimeLinkWins)
    {
        LifecycleClock clock = new();
        await using WorkerProxyWebApplicationFactory factory = CreateTimeoutFactory(
            clock,
            "00:01:00",
            replaceTimeProvider: false);
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpClient management = factory.CreateWorkerProxyClient();
        SplitWorkerOriginState initial = await ReadSplitManagementStateAsync(management, timeout.Token);
        await clock.WaitForTimerAsync(TimeSpan.FromMinutes(1), timeout.Token);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        LifecycleClock.BoundaryRace race = clock.ArmBoundaryRace(TimeSpan.FromMinutes(1), runtimeLinkWins);

        Task? acceptedRuntime = null;
        Task<GrpcRpcException>? rejectedRuntime = null;
        if (runtimeLinkWins)
        {
            acceptedRuntime = runtime.WriteAsync(CreateMessage("accepted-runtime-link"), timeout.Token);
        }
        else
        {
            rejectedRuntime = runtime.WriteAndReadRejectionAsync(
                CreateMessage("late-runtime-link"),
                timeout.Token);
        }

        await race.WaitForRuntimeAsync(timeout.Token);
        Task expiry = Task.Run(() => clock.Advance(TimeSpan.FromMinutes(1)));
        await race.WaitForTimerAsync(timeout.Token);
        Assert.False(expiry.IsCompleted);

        race.Release();
        await expiry.WaitAsync(timeout.Token);

        SplitWorkerOriginState selected = await ReadSplitManagementStateAsync(management, timeout.Token);
        if (runtimeLinkWins)
        {
            await acceptedRuntime!;
            Assert.Equal(initial, selected);
            await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);
            Assert.True(relay.IsAttached(FunctionRpcRelaySide.Runtime));
        }
        else
        {
            GrpcRpcException rejection = await rejectedRuntime!;
            Assert.Equal(StatusCode.Unavailable, rejection.StatusCode);
            Assert.False(relay.IsAttached(FunctionRpcRelaySide.Runtime));
            Assert.Equal(SplitWorkerStatus.RemovalRequested, selected.Status);
            Assert.Equal(initial.RevisionId + 1, selected.RevisionId);
        }

        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(selected, await ReadSplitManagementStateAsync(management, timeout.Token));
    }

    private static WorkerProxyWebApplicationFactory CreateTimeoutFactory(
        LifecycleClock clock,
        string? duration = null,
        Action<IServiceCollection>? configureServices = null,
        bool replaceTimeProvider = true)
    {
        Dictionary<string, string?> configuration = [];
        if (duration is not null)
        {
            configuration["WorkerProxy:NeverLinkedTimeout"] = duration;
        }

        WorkerProxyStartupTime startupTime = WorkerProxyStartupTime.Capture(clock);
        return new(configuration, services =>
        {
            if (replaceTimeProvider)
            {
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
            }

            services.Replace(ServiceDescriptor.Singleton(startupTime));
            configureServices?.Invoke(services);
        });
    }

    private sealed class LifecycleClock : TimeProvider
    {
        private readonly Lock _sync = new();
        private readonly List<ScheduledTimer> _timers = [];
        private readonly DateTimeOffset _origin = new(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);
        private TaskCompletionSource _timerChanged = CreateSignal();
        private BoundaryRace? _boundaryRace;
        private TimeSpan _elapsed;
        private TimeSpan _utcAdjustment;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_sync)
            {
                return _origin + _elapsed + _utcAdjustment;
            }
        }

        public override long GetTimestamp()
        {
            BoundaryRace? race = null;
            long timestamp;
            lock (_sync)
            {
                timestamp = _elapsed.Ticks;
                if (_boundaryRace is { TimestampClaimed: false } boundaryRace)
                {
                    boundaryRace.TimestampClaimed = true;
                    race = boundaryRace;
                }
            }

            if (race is null)
            {
                return timestamp;
            }

            race.TimestampEntered.TrySetResult();
            race.SignalAndWait();
            if (race.RuntimeLinkWins)
            {
                return timestamp;
            }

            lock (_sync)
            {
                return _elapsed.Ticks;
            }
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(callback);
            ScheduledTimer timer = new(this, callback, state);
            lock (_sync)
            {
                _timers.Add(timer);
                ChangeTimerLocked(timer, dueTime, period);
                NotifyTimerChangedLocked();
            }

            return timer;
        }

        public void Advance(TimeSpan amount)
        {
            List<ScheduledTimer> due = [];
            lock (_sync)
            {
                _elapsed += amount;
                foreach (ScheduledTimer timer in _timers)
                {
                    if (timer.Due is not { } deadline || deadline > _elapsed)
                    {
                        continue;
                    }

                    timer.Due = timer.Period == Timeout.InfiniteTimeSpan
                        ? null
                        : _elapsed + timer.Period;
                    due.Add(timer);
                }
            }

            foreach (ScheduledTimer timer in due)
            {
                BoundaryRace? race = null;
                lock (_sync)
                {
                    if (_boundaryRace is { TimerClaimed: false } boundaryRace &&
                        boundaryRace.IsTimer(timer))
                    {
                        boundaryRace.TimerClaimed = true;
                        race = boundaryRace;
                    }
                }

                if (race is not null)
                {
                    race.TimerEntered.TrySetResult();
                    race.SignalAndWait();
                }

                timer.Callback(timer.State);
            }
        }

        public void AdjustUtc(TimeSpan amount)
        {
            lock (_sync)
            {
                _utcAdjustment += amount;
            }
        }

        public async Task WaitForTimerAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            while (true)
            {
                Task timerChanged;
                lock (_sync)
                {
                    if (_timers.Exists(timer => timer.Due - _elapsed == delay))
                    {
                        return;
                    }

                    timerChanged = _timerChanged.Task;
                }

                await timerChanged.WaitAsync(cancellationToken);
            }
        }

        public BoundaryRace ArmBoundaryRace(TimeSpan delay, bool runtimeLinkWins)
        {
            lock (_sync)
            {
                if (_boundaryRace is not null)
                {
                    throw new InvalidOperationException("A boundary race is already armed.");
                }

                ScheduledTimer timer = _timers.Find(timer => timer.Due - _elapsed == delay)
                    ?? throw new InvalidOperationException($"No timer is scheduled for {delay}.");
                _boundaryRace = new(timer, runtimeLinkWins);
                return _boundaryRace;
            }
        }

        private bool ChangeTimer(ScheduledTimer timer, TimeSpan dueTime, TimeSpan period)
        {
            lock (_sync)
            {
                if (timer.Disposed)
                {
                    return false;
                }

                ChangeTimerLocked(timer, dueTime, period);
                NotifyTimerChangedLocked();
                return true;
            }
        }

        private void ChangeTimerLocked(ScheduledTimer timer, TimeSpan dueTime, TimeSpan period)
        {
            timer.Due = dueTime == Timeout.InfiniteTimeSpan ? null : _elapsed + dueTime;
            timer.Period = period;
        }

        private void NotifyTimerChangedLocked()
        {
            TaskCompletionSource timerChanged = _timerChanged;
            _timerChanged = CreateSignal();
            timerChanged.TrySetResult();
        }

        private static TaskCompletionSource CreateSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private void DisposeTimer(ScheduledTimer timer)
        {
            lock (_sync)
            {
                timer.Disposed = true;
                timer.Due = null;
            }
        }

        public sealed class BoundaryRace
        {
            private readonly Barrier _barrier = new(participantCount: 3);
            private readonly ScheduledTimer _timer;

            internal BoundaryRace(ScheduledTimer timer, bool runtimeLinkWins)
            {
                _timer = timer;
                RuntimeLinkWins = runtimeLinkWins;
            }

            public bool RuntimeLinkWins { get; }

            internal TaskCompletionSource TimestampEntered { get; } = CreateSignal();

            internal TaskCompletionSource TimerEntered { get; } = CreateSignal();

            internal bool TimestampClaimed { get; set; }

            internal bool TimerClaimed { get; set; }

            internal bool IsTimer(ScheduledTimer timer) => ReferenceEquals(_timer, timer);

            public Task WaitForRuntimeAsync(CancellationToken cancellationToken) =>
                TimestampEntered.Task.WaitAsync(cancellationToken);

            public Task WaitForTimerAsync(CancellationToken cancellationToken) =>
                TimerEntered.Task.WaitAsync(cancellationToken);

            public void Release() => _barrier.SignalAndWait();

            internal void SignalAndWait() => _barrier.SignalAndWait();
        }

        internal sealed class ScheduledTimer(
            LifecycleClock owner,
            TimerCallback callback,
            object? state) : ITimer
        {
            public TimerCallback Callback { get; } = callback;

            public object? State { get; } = state;

            public TimeSpan? Due { get; set; }

            public TimeSpan Period { get; set; }

            public bool Disposed { get; set; }

            public bool Change(TimeSpan dueTime, TimeSpan period) => owner.ChangeTimer(this, dueTime, period);

            public void Dispose() => owner.DisposeTimer(this);

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
