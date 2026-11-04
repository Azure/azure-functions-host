// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Management;
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    [Fact]
    public async Task RuntimeEventStream_EofBeforeWorkerInitializationRequestsRemoval()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpClient management = factory.CreateWorkerProxyClient();
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();

        await runtime.WriteAsync(CreateMessage("runtime-admitted"), timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);
        await runtime.CompleteRequestAsync(timeout.Token);

        Assert.Equal(StatusCode.Unavailable, await runtime.WaitForTerminationAsync(timeout.Token));
        SplitWorkerOriginState removed = await WaitForRemovalStateAsync(management, timeout.Token);
        Assert.Equal(SplitWorkerStatus.RemovalRequested, removed.Status);
        Assert.Equal(SplitWorkerLinkState.Unlinked, removed.Link.State);
    }

    [Fact]
    public async Task RuntimeEventStream_CancellationRequestsRemoval()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        using CancellationTokenSource timeout = new(TestTimeout);
        using CancellationTokenSource runtimeCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        using HttpClient management = factory.CreateWorkerProxyClient();
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, runtimeCancellation.Token);
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();

        await runtime.WriteAsync(CreateMessage("runtime-admitted"), timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);
        runtimeCancellation.Cancel();

        Assert.Equal(
            SplitWorkerStatus.RemovalRequested,
            (await WaitForRemovalStateAsync(management, timeout.Token)).Status);
        await WaitForReleaseAsync(relay, timeout.Token);
    }

    [Fact]
    public async Task ApplicationShutdown_DoesNotRequestRemoval()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpClient management = factory.CreateWorkerProxyClient();
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        await runtime.WriteAsync(CreateMessage("runtime-admitted"), timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);

        await relay.StopAsync(timeout.Token);

        Assert.Contains(
            await runtime.WaitForTerminationAsync(timeout.Token),
            new[] { StatusCode.Internal, StatusCode.Unavailable, StatusCode.Cancelled });
        Assert.Equal(
            SplitWorkerStatus.Starting,
            (await ReadSplitManagementStateAsync(management, timeout.Token)).Status);
    }

    [Theory]
    [InlineData("Preconfigured", null, 16)]
    [InlineData("Preconfigured", 10, 10)]
    [InlineData("SpecializationRequired", null, 16)]
    [InlineData("SpecializationRequired", 10, 10)]
    public async Task RemovalRequested_IsStickyPollableAndClosesNewAdmission(
        string startupMode, int? maxHttpConcurrency, int expectedConcurrency)
    {
        int forwardedRequests = 0;
        await using var destination = await WorkerHttpForwardingTests.StartWorkerAsync(context =>
        {
            Interlocked.Increment(ref forwardedRequests);
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });
        Dictionary<string, string?> configuration = new()
        {
            [$"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.WorkerHttpEndpoint)}"] =
                WorkerHttpForwardingTests.GetAddress(destination).AbsoluteUri
        };
        await using WorkerProxyWebApplicationFactory factory = new(configuration);
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpClient management = factory.CreateWorkerProxyClient();
        using HttpClient http = factory.CreateHttpForwardingClient();
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        WorkerAssignRequest assignment = new()
        {
            StartupMode = startupMode,
            FunctionAppName = "test-app",
            FunctionGroupName = "http",
            HttpProxyEndpoint = "worker-pod.example:48801",
            IsAlwaysReady = false,
            Environment = [],
            FunctionAppDirectory = "/home/site/wwwroot"
        };
        if (maxHttpConcurrency is { } concurrency)
        {
            assignment.MaxHttpConcurrency = concurrency;
        }

        StreamingMessage initialized = await InitializeAndAssignWorkerAsync(
            worker, runtime, management, assignment,
            WorkerHttpForwardingTests.GetAddress(destination).AbsoluteUri, timeout.Token);
        Assert.Equal("http://worker-pod.example:48801/", initialized.WorkerInitResponse.Capabilities["HttpUri"]);
        Assert.Equal("http", initialized.WorkerInitResponse.Capabilities[FunctionGroupNameCapability]);
        Assert.Equal(
            expectedConcurrency.ToString(CultureInfo.InvariantCulture),
            initialized.WorkerInitResponse.Capabilities[MaxHttpConcurrencyCapability]);
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        Assert.Equal(expectedConcurrency, manager.State.MaxHttpConcurrency);
        long sessionId = Assert.IsType<long>(manager.State.SessionId);
        DateTimeOffset startupTime = manager.State.StartupTime;

        using (HttpResponseMessage before = await http.GetAsync("/before-removal", timeout.Token))
        {
            Assert.Equal(HttpStatusCode.NoContent, before.StatusCode);
        }

        await runtime.CompleteRequestAsync(timeout.Token);
        await Task.WhenAll(
            runtime.WaitForTerminationAsync(timeout.Token),
            worker.WaitForTerminationAsync(timeout.Token));
        SplitWorkerOriginState removed = await WaitForRemovalStateAsync(management, timeout.Token);

        Assert.Equal(SplitWorkerStatus.RemovalRequested, removed.Status);
        Assert.Equal(SplitWorkerStatus.RemovalRequested, (await ReadSplitManagementStateAsync(management, timeout.Token)).Status);
        WorkerPodState removedState = manager.State;
        Assert.Null(removedState.FunctionAppName);
        Assert.Null(removedState.FunctionGroupName);
        Assert.Null(removedState.MaxHttpConcurrency);
        Assert.Null(manager.GetWorkerCapabilitySnapshot(sessionId).HttpProxyEndpoint);
        Assert.Equal(startupTime, removedState.StartupTime);
        Assert.False(manager.RequestRemoval());
        Assert.Same(removedState, manager.State);

        await AssertReadinessAsync(management, "/admin/instance/ready", HttpStatusCode.OK, timeout.Token);
        await AssertReadinessAsync(management, "/admin/worker/ready", HttpStatusCode.ServiceUnavailable, timeout.Token);
        using (HttpResponseMessage reassignment = await PutManagementJsonAsync(
            management,
            "/admin/worker/assignment",
            ManagementAssignment,
            timeout.Token))
        {
            await AssertManagementErrorAsync(reassignment, HttpStatusCode.ServiceUnavailable, "WorkerNotReady", timeout.Token);
        }

        using (HttpResponseMessage after = await http.GetAsync("/after-removal", timeout.Token))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, after.StatusCode);
        }

        Assert.Equal(1, Volatile.Read(ref forwardedRequests));
        foreach (FunctionRpcRelaySide side in new[] { FunctionRpcRelaySide.Runtime, FunctionRpcRelaySide.Worker })
        {
            await using RelayClient replacement = CreateClient(factory, side, timeout.Token);
            Grpc.Core.RpcException rejected = await replacement.WriteAndReadRejectionAsync(
                side == FunctionRpcRelaySide.Worker ? CreateStartStream("replacement") : CreateMessage("replacement"),
                timeout.Token);
            Assert.Equal(StatusCode.Unavailable, rejected.StatusCode);
        }

        Assert.Same(removedState, manager.State);
    }

    private static async Task<StreamingMessage> InitializeAndAssignWorkerAsync(
        RelayClient worker,
        RelayClient runtime,
        HttpClient management,
        WorkerAssignRequest assignment,
        string httpUri,
        CancellationToken cancellationToken)
    {
        StreamingMessage start = CreateStartStream();
        await worker.WriteAsync(start, cancellationToken);
        Assert.Equal(start, await runtime.ReadAsync(cancellationToken));
        using (HttpResponseMessage assigned = await management.PutAsJsonAsync(
            "/admin/worker/assignment", assignment, WorkerProxyJsonContext.Default.WorkerAssignRequest, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Created, assigned.StatusCode);
        }

        StreamingMessage request = new()
        {
            RequestId = "worker-init",
            WorkerInitRequest = new() { HostVersion = "test-host" }
        };
        await runtime.WriteAsync(request, cancellationToken);
        Assert.Equal(request, await worker.ReadAsync(cancellationToken));
        await worker.WriteAsync(CreateInitResponse(request.RequestId, httpUri), cancellationToken);
        StreamingMessage initialized = await runtime.ReadAsync(cancellationToken);
        Assert.NotNull(initialized.WorkerInitResponse);
        return initialized;
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
}
