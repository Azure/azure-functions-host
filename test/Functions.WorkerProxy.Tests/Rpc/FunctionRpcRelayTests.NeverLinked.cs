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
        await using WorkerProxyWebApplicationFactory factory = CreateTimeoutFactory(clock, "00:02:00");
        clock.Advance(TimeSpan.FromSeconds(90));
        clock.AdjustUtc(TimeSpan.FromDays(1));
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpClient management = factory.CreateWorkerProxyClient();

        SplitWorkerOriginState initial = await ReadSplitManagementStateAsync(management, timeout.Token);

        Assert.Equal(processStart, initial.StartupTime);
        Assert.Equal(SplitWorkerStatus.Starting, initial.Status);
        await clock.WaitForTimerAsync(TimeSpan.FromSeconds(30), timeout.Token);
        clock.Advance(TimeSpan.FromSeconds(30) - TimeSpan.FromMilliseconds(1));
        Assert.Equal(initial, await ReadSplitManagementStateAsync(management, timeout.Token));

        clock.Advance(TimeSpan.FromMilliseconds(1));
        SplitWorkerOriginState removed = await ReadSplitManagementStateAsync(management, timeout.Token);

        Assert.Equal(SplitWorkerStatus.RemovalRequested, removed.Status);
        Assert.Equal(processStart, removed.StartupTime);
        Assert.Equal(initial.RevisionId + 1, removed.RevisionId);
    }

    [Fact]
    public async Task NeverLinkedDeadline_ClosesPublicAdmissionAndPublishesStickyRemoval()
    {
        int forwardedRequests = 0;
        await using var destination = await WorkerHttpForwardingTests.StartWorkerAsync(_ =>
        {
            Interlocked.Increment(ref forwardedRequests);
            return Task.CompletedTask;
        });
        LifecycleClock clock = new();
        await using WorkerProxyWebApplicationFactory factory = CreateTimeoutFactory(
            clock,
            httpEndpoint: WorkerHttpForwardingTests.GetAddress(destination).AbsoluteUri);
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpClient management = factory.CreateWorkerProxyClient();
        using HttpClient http = factory.CreateHttpForwardingClient();
        await using RelayClient abandonedWorker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await abandonedWorker.WriteAsync(CreateStartStream(), timeout.Token);
        await WaitForWorkerReadyAsync(manager, timeout.Token);
        using (HttpResponseMessage beforeDeadline = await http.GetAsync("/before-deadline", timeout.Token))
        {
            Assert.Equal(HttpStatusCode.OK, beforeDeadline.StatusCode);
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
        await AssertReadinessAsync(management, "/admin/worker/ready", HttpStatusCode.ServiceUnavailable, timeout.Token);
        using (HttpResponseMessage assignment = await PutManagementJsonAsync(
            management,
            "/admin/worker/assignment",
            ManagementAssignment,
            timeout.Token))
        {
            await AssertManagementErrorAsync(
                assignment,
                HttpStatusCode.Conflict,
                "RemovalRequested",
                timeout.Token);
        }

        await using RelayClient lateWorker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        GrpcRpcException workerRejection = await lateWorker.WriteAndReadRejectionAsync(
            CreateStartStream(),
            timeout.Token);
        Assert.Equal(StatusCode.Unavailable, workerRejection.StatusCode);
        await using RelayClient lateRuntime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        GrpcRpcException runtimeRejection = await lateRuntime.WriteAndReadRejectionAsync(
            new StreamingMessage { WorkerInitRequest = new() { HostVersion = "late-runtime" } },
            timeout.Token);
        Assert.Equal(StatusCode.Unavailable, runtimeRejection.StatusCode);
        using (HttpResponseMessage afterDeadline = await http.GetAsync("/after-deadline", timeout.Token))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, afterDeadline.StatusCode);
        }

        Assert.Equal(1, Volatile.Read(ref forwardedRequests));
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(removed, await ReadSplitManagementStateAsync(management, timeout.Token));
    }

    [Fact]
    public async Task NeverLinkedDeadline_AssignmentFailureAndReloadDoNotResetDeadline()
    {
        LifecycleClock clock = new();
        await using WorkerProxyWebApplicationFactory factory = CreateTimeoutFactory(clock, "00:02:00");
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpClient management = factory.CreateWorkerProxyClient();
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await clock.WaitForTimerAsync(TimeSpan.FromMinutes(2), timeout.Token);
        clock.Advance(TimeSpan.FromMinutes(1));
        await worker.WriteAsync(CreateStartStream(), timeout.Token);
        await runtime.ReadAsync(timeout.Token);
        StreamingMessage initialization = new() { WorkerInitRequest = new() { HostVersion = "test-host" } };
        await runtime.WriteAsync(initialization, timeout.Token);
        await worker.ReadAsync(timeout.Token);
        using (HttpResponseMessage assignment = await PutManagementJsonAsync(
            management,
            "/admin/worker/assignment",
            ManagementAssignment,
            timeout.Token))
        {
            Assert.Equal(HttpStatusCode.Created, assignment.StatusCode);
        }

        await worker.WriteAsync(new StreamingMessage
        {
            WorkerInitResponse = new() { Result = new() { Status = StatusResult.Types.Status.Failure } }
        }, timeout.Token);
        await runtime.ReadAsync(timeout.Token);
        using (HttpResponseMessage replay = await PutManagementJsonAsync(
            management,
            "/admin/worker/assignment",
            ManagementAssignment,
            timeout.Token))
        {
            Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        }

        IConfiguration configuration = factory.Services.GetRequiredService<IConfiguration>();
        configuration["WorkerProxy:NeverLinkedTimeout"] = "00:30:00";
        ((IConfigurationRoot)configuration).Reload();
        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(
            SplitWorkerStatus.RemovalRequested,
            (await ReadSplitManagementStateAsync(management, timeout.Token)).Status);
        await worker.WriteAsync(CreateInitResponse("late-success", httpUri: null), timeout.Token);
        Assert.Equal(StatusCode.Unavailable, await runtime.WaitForTerminationAsync(timeout.Token));
        Assert.Equal(StatusCode.Unavailable, await worker.WaitForTerminationAsync(timeout.Token));
    }

    [Fact]
    public async Task NeverLinkedDeadline_SuccessfulInitializationCancelsExpiry()
    {
        LifecycleClock clock = new();
        await using WorkerProxyWebApplicationFactory factory = CreateTimeoutFactory(clock, "00:01:00");
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpClient management = factory.CreateWorkerProxyClient();
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await InitializeLinkAsync(worker, runtime, timeout.Token);
        SplitWorkerOriginState accepted = await ReadSplitManagementStateAsync(management, timeout.Token);

        clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(accepted, await ReadSplitManagementStateAsync(management, timeout.Token));
        StreamingMessage afterDeadline = new() { RequestId = "after-deadline" };
        await runtime.WriteAsync(afterDeadline, timeout.Token);
        Assert.Equal(afterDeadline, await worker.ReadAsync(timeout.Token));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NeverLinkedDeadline_BoundarySelectsOneStickyOutcome(bool initializationWins)
    {
        LifecycleClock clock = new();
        await using WorkerProxyWebApplicationFactory factory = CreateTimeoutFactory(clock, "00:01:00");
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpClient management = factory.CreateWorkerProxyClient();
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await worker.WriteAsync(CreateStartStream(), timeout.Token);
        await runtime.ReadAsync(timeout.Token);
        await runtime.WriteAsync(
            new StreamingMessage { WorkerInitRequest = new() { HostVersion = "test-host" } },
            timeout.Token);
        await worker.ReadAsync(timeout.Token);

        if (initializationWins)
        {
            await worker.WriteAsync(CreateInitResponse("accepted", httpUri: null), timeout.Token);
            Assert.NotNull((await runtime.ReadAsync(timeout.Token)).WorkerInitResponse);
            clock.Advance(TimeSpan.FromMinutes(1));
        }
        else
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            await worker.WriteAsync(CreateInitResponse("late", httpUri: null), timeout.Token);
            Assert.Equal(StatusCode.Unavailable, await runtime.WaitForTerminationAsync(timeout.Token));
            Assert.Equal(StatusCode.Unavailable, await worker.WaitForTerminationAsync(timeout.Token));
        }

        SplitWorkerOriginState selected = await ReadSplitManagementStateAsync(management, timeout.Token);
        Assert.Equal(
            initializationWins ? SplitWorkerStatus.Starting : SplitWorkerStatus.RemovalRequested,
            selected.Status);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(selected, await ReadSplitManagementStateAsync(management, timeout.Token));
    }

    private static WorkerProxyWebApplicationFactory CreateTimeoutFactory(
        LifecycleClock clock,
        string? duration = null,
        string? httpEndpoint = null)
    {
        Dictionary<string, string?> configuration = [];
        if (duration is not null)
        {
            configuration["WorkerProxy:NeverLinkedTimeout"] = duration;
        }

        if (httpEndpoint is not null)
        {
            configuration["WorkerProxy:WorkerHttpEndpoint"] = httpEndpoint;
        }

        WorkerProxyStartupTime startupTime = WorkerProxyStartupTime.Capture(clock);
        return new(configuration, services =>
        {
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
            services.Replace(ServiceDescriptor.Singleton(startupTime));
        });
    }

    private static async Task InitializeLinkAsync(
        RelayClient worker,
        RelayClient runtime,
        CancellationToken cancellationToken)
    {
        await worker.WriteAsync(CreateStartStream(), cancellationToken);
        await runtime.ReadAsync(cancellationToken);
        await runtime.WriteAsync(
            new StreamingMessage { WorkerInitRequest = new() { HostVersion = "test-host" } },
            cancellationToken);
        await worker.ReadAsync(cancellationToken);
        await worker.WriteAsync(CreateInitResponse("accepted", httpUri: null), cancellationToken);
        Assert.NotNull((await runtime.ReadAsync(cancellationToken)).WorkerInitResponse);
    }

    private sealed class LifecycleClock : TimeProvider
    {
        private readonly Lock _sync = new();
        private readonly List<ScheduledTimer> _timers = [];
        private readonly DateTimeOffset _origin = new(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);
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
                lock (_sync)
                {
                    if (_timers.Exists(timer => timer.Due - _elapsed == delay))
                    {
                        return;
                    }
                }

                await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
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
                return true;
            }
        }

        private void ChangeTimerLocked(ScheduledTimer timer, TimeSpan dueTime, TimeSpan period)
        {
            timer.Due = dueTime == Timeout.InfiniteTimeSpan ? null : _elapsed + dueTime;
            timer.Period = period;
        }

        private void DisposeTimer(ScheduledTimer timer)
        {
            lock (_sync)
            {
                timer.Disposed = true;
                timer.Due = null;
            }
        }

        private sealed class ScheduledTimer(
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
