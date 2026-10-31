// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Xunit;
using GrpcRpcException = Grpc.Core.RpcException;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    [Fact]
    public async Task WorkerGrpcDeadline_NoConnectionRequestsStickyRemovalAndClosesAdmissions()
    {
        await using WebApplication workerApp = await WorkerHttpForwardingTests.StartWorkerAsync(context =>
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });
        WorkerEstablishmentClock clock = new();
        Dictionary<string, string?> configuration = new()
        {
            [$"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.WorkerHttpEndpoint)}"] =
                WorkerHttpForwardingTests.GetAddress(workerApp).AbsoluteUri
        };
        await using WorkerProxyWebApplicationFactory factory = CreateWorkerEstablishmentFactory(clock, configuration);
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using HttpClient forwarding = factory.CreateHttpForwardingClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await runtime.WriteAsync(CreateMessage("runtime-link"), timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);
        using (HttpResponseMessage admitted = await forwarding.GetAsync("/invoke", timeout.Token))
        {
            Assert.Equal(HttpStatusCode.NoContent, admitted.StatusCode);
        }

        Task<GrpcRpcException> runtimeTermination = runtime.WaitForTerminationExceptionAsync(timeout.Token);
        await clock.FireDeadlineAsync(timeout.Token);

        GrpcRpcException runtimeException = await runtimeTermination;
        Assert.Equal(StatusCode.Unavailable, runtimeException.StatusCode);
        AssertRemovalTrailer(runtimeException);
        SplitWorkerOriginState first = await WaitForRemovalStateAsync(management, timeout.Token);
        using HttpResponseMessage repeatedResponse = await PollSplitManagementStateAsync(management, first, timeout.Token);
        SplitWorkerOriginState repeated = await ReadSplitManagementStateAsync(repeatedResponse, timeout.Token);
        Assert.Equal(SplitWorkerStatus.RemovalRequested, repeated.Status);
        Assert.Equal(first.RevisionId, repeated.RevisionId);
        Assert.True(repeated.IsUnhealthy);
        await AssertReadinessAsync(management, "/admin/worker/ready", HttpStatusCode.ServiceUnavailable, timeout.Token);
        await AssertReadinessAsync(management, "/admin/instance/ready", HttpStatusCode.OK, timeout.Token);
        using (HttpResponseMessage assignment = await PutManagementJsonAsync(
            management, "/admin/worker/assignment", ManagementAssignment, timeout.Token))
        {
            await AssertManagementErrorAsync(assignment, HttpStatusCode.Conflict, "RemovalRequested", timeout.Token);
        }

        using (HttpResponseMessage rejected = await forwarding.GetAsync("/invoke", timeout.Token))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
        }

        await AssertLateStreamRejectedAsync(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await AssertLateStreamRejectedAsync(factory, FunctionRpcRelaySide.Worker, timeout.Token);
    }

    [Fact]
    public async Task WorkerGrpcDeadline_AttachedStreamWithoutStartStreamExpiresAndCannotReconnect()
    {
        WorkerEstablishmentClock clock = new();
        await using WorkerProxyWebApplicationFactory factory = CreateWorkerEstablishmentFactory(clock);
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        Task<GrpcRpcException> workerTermination = worker.WaitForTerminationExceptionAsync(timeout.Token);
        await WaitForRevisionAsync(management, expectedRevision: 1, timeout.Token);

        await clock.FireDeadlineAsync(timeout.Token);

        GrpcRpcException workerException = await workerTermination;
        Assert.Equal(StatusCode.Unavailable, workerException.StatusCode);
        SplitWorkerOriginState removed = await WaitForRemovalStateAsync(management, timeout.Token);
        Assert.Equal(SplitWorkerStatus.RemovalRequested, removed.Status);
        await AssertLateStreamRejectedAsync(factory, FunctionRpcRelaySide.Worker, timeout.Token);
    }

    [Fact]
    public async Task WorkerGrpcDeadline_ValidStartStreamCancelsEstablishmentTimeout()
    {
        WorkerEstablishmentClock clock = new();
        await using WorkerProxyWebApplicationFactory factory = CreateWorkerEstablishmentFactory(clock);
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        await clock.WaitForDeadlineTimerAsync(timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);

        await worker.WriteAsync(CreateStartStream(), timeout.Token);

        await AssertReadinessAsync(management, "/admin/worker/ready", HttpStatusCode.OK, timeout.Token);
        await clock.WaitForDeadlineTimerDisposalAsync(timeout.Token);
        using (HttpResponseMessage assignment = await PutManagementJsonAsync(
            management, "/admin/worker/assignment", ManagementAssignment, timeout.Token))
        {
            Assert.Equal(HttpStatusCode.Created, assignment.StatusCode);
        }

        SplitWorkerOriginState state = await ReadSplitManagementStateAsync(management, timeout.Token);
        Assert.Equal(SplitWorkerStatus.Starting, state.Status);
        Assert.False(state.IsUnhealthy);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        Assert.Equal("test-worker", (await runtime.ReadAsync(timeout.Token)).StartStream.WorkerId);
        StreamingMessage runtimeMessage = CreateMessage("runtime-after-start");
        await runtime.WriteAsync(runtimeMessage, timeout.Token);
        Assert.Equal(runtimeMessage, await worker.ReadAsync(timeout.Token));
    }

    [Fact]
    public async Task WorkerGrpcTerminalOutcome_RequestsStickyRemoval()
    {
        string[] triggers = ["invalid-first", "prestart-eof", "established-eof", "established-cancel"];
        foreach (string trigger in triggers)
        {
            await AssertWorkerGrpcTerminalOutcomeRequestsStickyRemovalAsync(trigger);
        }
    }

    private static async Task AssertWorkerGrpcTerminalOutcomeRequestsStickyRemovalAsync(string trigger)
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        using CancellationTokenSource workerCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, workerCancellation.Token);
        await runtime.WriteAsync(CreateMessage("runtime-link"), timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);
        bool established = trigger.StartsWith("established", StringComparison.Ordinal);
        if (established)
        {
            await worker.WriteAsync(CreateStartStream(), timeout.Token);
            await AssertReadinessAsync(management, "/admin/worker/ready", HttpStatusCode.OK, timeout.Token);
            await AssignWorkerAsync(management, timeout.Token);
        }

        Task<GrpcRpcException> runtimeTermination = runtime.WaitForTerminationExceptionAsync(timeout.Token);
        switch (trigger)
        {
            case "invalid-first":
                await worker.WriteAsync(CreateMessage("invalid-first"), timeout.Token);
                break;
            case "prestart-eof":
            case "established-eof":
                await worker.CompleteRequestAsync(timeout.Token);
                break;
            case "established-cancel":
                workerCancellation.Cancel();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(trigger));
        }

        GrpcRpcException runtimeException = await runtimeTermination;
        StatusCode expectedStatus = string.Equals(trigger, "established-cancel", StringComparison.Ordinal)
            ? StatusCode.Cancelled
            : StatusCode.Unavailable;
        Assert.Equal(expectedStatus, runtimeException.StatusCode);
        AssertRemovalTrailer(runtimeException);
        SplitWorkerOriginState removed = await WaitForRemovalStateAsync(management, timeout.Token);
        Assert.Equal(SplitWorkerStatus.RemovalRequested, removed.Status);
        Assert.True(removed.IsUnhealthy);
        Assert.Equal(established ? "test-group" : null, removed.FunctionGroup);
        await AssertReadinessAsync(management, "/admin/worker/ready", HttpStatusCode.ServiceUnavailable, timeout.Token);
        using (HttpResponseMessage assignment = await PutManagementJsonAsync(
            management, "/admin/worker/assignment", ManagementAssignment, timeout.Token))
        {
            await AssertManagementErrorAsync(assignment, HttpStatusCode.Conflict, "RemovalRequested", timeout.Token);
        }

        await AssertLateStreamRejectedAsync(factory, FunctionRpcRelaySide.Worker, timeout.Token);
    }

    [Fact]
    public async Task WorkerGrpcDeadline_ApplicationShutdownDoesNotPublishRemoval()
    {
        WorkerEstablishmentClock clock = new();
        WorkerProxyWebApplicationFactory factory = CreateWorkerEstablishmentFactory(clock);
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        await clock.WaitForDeadlineTimerAsync(timeout.Token);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await runtime.WriteAsync(CreateMessage("runtime-link"), timeout.Token);
        await worker.WriteAsync(CreateStartStream(), timeout.Token);
        await AssertReadinessAsync(management, "/admin/worker/ready", HttpStatusCode.OK, timeout.Token);
        SplitWorkerOriginState beforeShutdown = await ReadSplitManagementStateAsync(management, timeout.Token);
        Assert.Equal(SplitWorkerStatus.Starting, beforeShutdown.Status);

        await factory.DisposeAsync();

        await clock.WaitForDeadlineTimerDisposalAsync(timeout.Token);
        Assert.False(manager.State.RemovalRequested);
        Assert.True(manager.State.Revision >= beforeShutdown.RevisionId);
    }

    private static WorkerProxyWebApplicationFactory CreateWorkerEstablishmentFactory(
        WorkerEstablishmentClock clock,
        IReadOnlyDictionary<string, string?>? configuration = null)
    {
        Dictionary<string, string?> values = configuration is null ? [] : new(configuration);
        values[$"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.WorkerGrpcEstablishmentTimeout)}"] = "00:01:00";
        return new(values, services =>
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock.Provider)));
    }

    private static async Task AssertLateStreamRejectedAsync(
        WorkerProxyWebApplicationFactory factory,
        FunctionRpcRelaySide side,
        CancellationToken cancellationToken)
    {
        await using RelayClient client = CreateClient(factory, side, cancellationToken);
        StreamingMessage message = side == FunctionRpcRelaySide.Worker
            ? CreateStartStream("late-worker")
            : CreateMessage("late-runtime");
        GrpcRpcException exception = await client.WriteAndReadRejectionAsync(message, cancellationToken);
        Assert.Equal(StatusCode.Unavailable, exception.StatusCode);
    }

    private static void AssertRemovalTrailer(GrpcRpcException exception)
    {
        Metadata.Entry trailer = Assert.Single(exception.Trailers, entry =>
            string.Equals(entry.Key, FunctionRpcRelayConstants.WorkerRemovalRequestedTrailerName, StringComparison.Ordinal));
        Assert.Equal(FunctionRpcRelayConstants.WorkerRemovalRequestedTrailerValue, trailer.Value);
    }

    private static async Task WaitForRevisionAsync(
        HttpClient management,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            using HttpResponseMessage response = await management.GetAsync("/admin/worker/state", cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using JsonDocument state = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (state.RootElement.GetProperty("revisionId").GetInt64() >= expectedRevision)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }

    private static async Task<SplitWorkerOriginState> WaitForRemovalStateAsync(
        HttpClient management,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            SplitWorkerOriginState state = await ReadSplitManagementStateAsync(management, cancellationToken);
            if (state.Status == SplitWorkerStatus.RemovalRequested)
            {
                return state;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }

    private sealed class WorkerEstablishmentClock
    {
        private readonly Mock<TimeProvider> _provider = new();
        private readonly TaskCompletionSource<ScheduledTimer> _deadlineTimer =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private long _timestamp;

        public WorkerEstablishmentClock()
        {
            _provider.Setup(instance => instance.GetTimestamp()).Returns(() => Volatile.Read(ref _timestamp));
            _provider.SetupGet(instance => instance.TimestampFrequency).Returns(TimeSpan.TicksPerSecond);
            _provider.Setup(instance => instance.GetUtcNow()).Returns(() =>
                DateTimeOffset.UnixEpoch.AddTicks(Volatile.Read(ref _timestamp)));
            _provider.Setup(instance => instance.CreateTimer(
                    It.IsAny<TimerCallback>(),
                    It.IsAny<object?>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<TimeSpan>()))
                .Returns((TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
                {
                    ScheduledTimer timer = new(callback, state, dueTime, period);
                    timer.Timer.Setup(instance => instance.Change(It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>())).Returns(true);
                    timer.Timer.Setup(instance => instance.Dispose()).Callback(() => timer.Disposed.TrySetResult());
                    _deadlineTimer.TrySetResult(timer);
                    return timer.Timer.Object;
                });
        }

        public TimeProvider Provider => _provider.Object;

        public async Task FireDeadlineAsync(CancellationToken cancellationToken)
        {
            ScheduledTimer timer = await WaitForDeadlineTimerAsync(cancellationToken);
            Assert.Equal(TimeSpan.FromSeconds(60), timer.DueTime);
            Assert.Equal(Timeout.InfiniteTimeSpan, timer.Period);
            Interlocked.Exchange(ref _timestamp, timer.DueTime.Ticks);
            timer.Callback(timer.State);
        }

        public Task<ScheduledTimer> WaitForDeadlineTimerAsync(CancellationToken cancellationToken) =>
            _deadlineTimer.Task.WaitAsync(cancellationToken);

        public async Task WaitForDeadlineTimerDisposalAsync(CancellationToken cancellationToken)
        {
            ScheduledTimer timer = await WaitForDeadlineTimerAsync(cancellationToken);
            await timer.Disposed.Task.WaitAsync(cancellationToken);
        }

        public sealed record ScheduledTimer(
            TimerCallback Callback,
            object? State,
            TimeSpan DueTime,
            TimeSpan Period)
        {
            public Mock<ITimer> Timer { get; } = new();

            public TaskCompletionSource Disposed { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
