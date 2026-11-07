// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Logging;
using Azure.Functions.WorkerProxy.Management;
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using GrpcRpcException = Grpc.Core.RpcException;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    [Theory]
    [InlineData(299, true)]
    [InlineData(300, false)]
    public void NeverLinkedDeadline_StartsAtManagerConstruction(int elapsedSeconds, bool accepted)
    {
        ControlledTimeProvider clock = new();
        clock.Advance(TimeSpan.FromMinutes(10));
        DateTimeOffset startupTime = clock.GetUtcNow();
        WorkerPodStateManager manager = new(
            Options.Create(new WorkerProxyOptions
            {
                PodName = "test-pod",
                NeverLinkedTimeout = TimeSpan.FromMinutes(5)
            }),
            clock);

        clock.Advance(TimeSpan.FromSeconds(elapsedSeconds));

        Assert.Equal(accepted, manager.OnRuntimeAttached(1, null));
        Assert.Equal(!accepted, manager.State.RemovalRequested);
        Assert.Equal(startupTime, manager.StartupTime);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(nameof(WorkerStartupMode.Preconfigured), null)]
    [InlineData(nameof(WorkerStartupMode.Preconfigured), 10)]
    [InlineData(nameof(WorkerStartupMode.SpecializationRequired), null)]
    [InlineData(nameof(WorkerStartupMode.SpecializationRequired), 10)]
    public async Task NeverLinkedDeadline_PublishesStickyRemovalAndClosesPodAdmission(
        string? startupMode, int? maxHttpConcurrency)
    {
        ControlledTimeProvider clock = new();
        CapturingEventGenerator eventGenerator = new();
        int forwardedRequests = 0;
        await using WebApplication workerApp = await WorkerHttpForwardingTests.StartWorkerAsync(context =>
        {
            Interlocked.Increment(ref forwardedRequests);
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            return Task.CompletedTask;
        });
        await using WorkerProxyWebApplicationFactory factory =
            CreateTimeoutFactory(clock, WorkerHttpForwardingTests.GetAddress(workerApp), eventGenerator);
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpClient management = factory.CreateWorkerProxyClient();
        using HttpClient forwarding = factory.CreateHttpForwardingClient();
        await clock.WaitForTimerAsync(timeout.Token);

        WorkerAssignRequest assignment = new()
        {
            StartupMode = startupMode ?? nameof(WorkerStartupMode.Preconfigured),
            FunctionAppName = "test-app",
            FunctionGroupName = "http",
            HttpProxyEndpoint = forwarding.BaseAddress!.Authority,
            IsAlwaysReady = false,
            Environment = [],
            FunctionAppDirectory = "/home/site/wwwroot"
        };
        if (maxHttpConcurrency is { } concurrency)
        {
            assignment.MaxHttpConcurrency = concurrency;
        }

        if (startupMode is not null)
        {
            using HttpResponseMessage response = await management.PutAsJsonAsync(
                "/admin/worker/assignment", assignment, WorkerProxyJsonContext.Default.WorkerAssignRequest, timeout.Token);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(maxHttpConcurrency ?? 16, manager.State.MaxHttpConcurrency);
        }

        WorkerOriginState removed;
        await using (RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token))
        {
            await worker.WriteAsync(CreateStartStream(), timeout.Token);
            await WaitForWorkerReadyAsync(manager, timeout.Token);
            WorkerPodState initialState = manager.State;
            WorkerOriginState initial = await ReadManagementStateAsync(management, timeout.Token);
            if (startupMode is not null)
            {
                Assert.Equal(
                    forwarding.BaseAddress.AbsoluteUri,
                    manager.GetWorkerCapabilitySnapshot(initialState.SessionId!.Value).HttpProxyEndpoint);
            }

            using (HttpResponseMessage response = await forwarding.GetAsync("/before-removal", timeout.Token))
            {
                Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            }

            clock.Advance(TimeSpan.FromMinutes(5));
            await clock.FireTimerAsync(timeout.Token);
            await WaitForRemovalAsync(manager, timeout.Token);
            removed = await ReadManagementStateAsync(management, timeout.Token);

            Assert.Equal(WorkerStatus.RemovalRequested, removed.Status);
            Assert.True(removed.IsUnhealthy);
            Assert.Equal(initial.RevisionId + 1, removed.RevisionId);
            Assert.Equal(0, manager.State.MaxHttpConcurrency);
            Assert.Null(manager.GetWorkerCapabilitySnapshot(initialState.SessionId!.Value).HttpProxyEndpoint);
            Assert.Equal(initialState with
            {
                Revision = initialState.Revision + 1,
                RemovalRequested = true,
                HttpProxyEndpoint = null,
                MaxHttpConcurrency = 0
            }, manager.State);
            Assert.False(manager.State.IsWorkerReady);
            using (HttpResponseMessage response = await forwarding.GetAsync("/after-removal", timeout.Token))
            {
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            }

            Assert.Equal(1, forwardedRequests);

            using HttpResponseMessage repeatedResponse =
                await PollManagementStateAsync(management, removed, timeout.Token);
            Assert.Equal(removed, await ReadManagementStateAsync(repeatedResponse, timeout.Token));

            using HttpResponseMessage assignmentResponse = await management.PutAsJsonAsync(
                "/admin/worker/assignment", assignment, WorkerProxyJsonContext.Default.WorkerAssignRequest, timeout.Token);
            if (startupMode is null)
            {
                await AssertManagementErrorAsync(
                    assignmentResponse,
                    HttpStatusCode.ServiceUnavailable,
                    "WorkerNotReady",
                    timeout.Token);
            }
            else
            {
                Assert.Equal(HttpStatusCode.NoContent, assignmentResponse.StatusCode);
            }

            Assert.Equal(0, manager.State.MaxHttpConcurrency);
            Assert.Equal(initialState.StartupTime, manager.State.StartupTime);
            Assert.True(manager.State.RemovalRequested);

            await worker.CompleteRequestAsync(timeout.Token);
            Assert.Equal(StatusCode.Unavailable, await worker.WaitForTerminationAsync(timeout.Token));
        }

        await using RelayClient lateRuntime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        GrpcRpcException runtimeRejection = await lateRuntime.WriteAndReadRejectionAsync(
            CreateMessage("late-runtime"),
            timeout.Token);
        Assert.Equal(StatusCode.Unavailable, runtimeRejection.StatusCode);

        await using RelayClient lateWorker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        GrpcRpcException workerRejection = await lateWorker.WriteAndReadRejectionAsync(
            CreateStartStream("late-worker"),
            timeout.Token);
        Assert.Equal(StatusCode.Unavailable, workerRejection.StatusCode);

        await WaitForLogAsync(() => eventGenerator.ProxyRecords.Count(record =>
            string.Equals(record.EventName, "AttachmentRejected", StringComparison.Ordinal)) == 2, timeout.Token);
        Assert.All(eventGenerator.ProxyRecords.Where(record =>
            string.Equals(record.EventName, "AttachmentRejected", StringComparison.Ordinal)), record =>
        {
            Assert.Equal(LogLevel.Warning, record.Level);
            Assert.Contains(nameof(FunctionRpcRelayAttachmentFailure.PodStateRejected), record.Message, StringComparison.Ordinal);
        });
        Assert.Equal(
            WorkerStatus.RemovalRequested,
            (await ReadManagementStateAsync(management, timeout.Token)).Status);
        Assert.Equal(0, manager.State.MaxHttpConcurrency);
    }

    [Fact]
    public async Task RuntimeConnectionBeforeDeadline_PreventsNeverLinkedRemoval()
    {
        ControlledTimeProvider clock = new();
        await using WorkerProxyWebApplicationFactory factory = CreateTimeoutFactory(clock);
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpClient management = factory.CreateWorkerProxyClient();
        await clock.WaitForTimerAsync(timeout.Token);
        WorkerOriginState initial = await ReadManagementStateAsync(management, timeout.Token);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);

        await runtime.WriteAsync(CreateMessage("runtime-link"), timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);
        clock.Advance(TimeSpan.FromMinutes(30));
        await clock.FireTimerAsync(timeout.Token);

        Assert.Equal(initial, await ReadManagementStateAsync(management, timeout.Token));
        Assert.False(factory.Services.GetRequiredService<WorkerPodStateManager>().State.RemovalRequested);
    }

    private static WorkerProxyWebApplicationFactory CreateTimeoutFactory(
        ControlledTimeProvider clock,
        Uri? workerHttpEndpoint = null,
        CapturingEventGenerator? eventGenerator = null)
    {
        Dictionary<string, string?> configuration = new()
        {
            [$"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.NeverLinkedTimeout)}"] = "00:05:00"
        };
        if (workerHttpEndpoint is not null)
        {
            configuration[$"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.WorkerHttpEndpoint)}"] =
                workerHttpEndpoint.AbsoluteUri;
        }

        return new(configuration, services =>
        {
            services.Replace(ServiceDescriptor.Singleton<WorkerPodStateManager>(provider =>
                new(provider.GetRequiredService<IOptions<WorkerProxyOptions>>(), clock)));
            if (eventGenerator is not null)
            {
                services.Replace(ServiceDescriptor.Singleton<IWorkerSystemEventGenerator>(eventGenerator));
            }
        });
    }

    private static async Task WaitForRemovalAsync(
        WorkerPodStateManager manager,
        CancellationToken cancellationToken)
    {
        while (!manager.State.RemovalRequested)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }

    private sealed class ControlledTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _origin = new(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);
        private readonly TaskCompletionSource<ManualTimer> _timerCreated =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private long _elapsedTicks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() =>
            _origin + TimeSpan.FromTicks(Interlocked.Read(ref _elapsedTicks));

        public override long GetTimestamp() => Interlocked.Read(ref _elapsedTicks);

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(callback);
            ManualTimer timer = new(callback, state);
            _timerCreated.TrySetResult(timer);
            return timer;
        }

        public void Advance(TimeSpan amount) => Interlocked.Add(ref _elapsedTicks, amount.Ticks);

        public Task WaitForTimerAsync(CancellationToken cancellationToken) =>
            _timerCreated.Task.WaitAsync(cancellationToken);

        public async Task FireTimerAsync(CancellationToken cancellationToken)
        {
            ManualTimer timer = await _timerCreated.Task.WaitAsync(cancellationToken);
            timer.Fire();
        }

        private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
        {
            private int _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period) => Volatile.Read(ref _disposed) == 0;

            public void Dispose() => Interlocked.Exchange(ref _disposed, 1);

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public void Fire()
            {
                if (Volatile.Read(ref _disposed) == 0)
                {
                    callback(state);
                }
            }
        }
    }
}
