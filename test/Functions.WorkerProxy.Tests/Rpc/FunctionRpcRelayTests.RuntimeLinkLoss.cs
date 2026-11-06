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
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RuntimeEventStream_EofBeforeWorkerInitializationRequestsRemoval(bool attachWorker, bool sendProtocolInput)
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpClient management = factory.CreateWorkerProxyClient();
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        await using RelayClient? worker = attachWorker ? CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token) : null;
        if (worker is not null)
        {
            await worker.WriteAsync(CreateStartStream(), timeout.Token);
            Assert.NotNull((await runtime.ReadAsync(timeout.Token)).StartStream);
        }

        if (sendProtocolInput)
        {
            await runtime.WriteAsync(CreateMessage("runtime-admitted"), timeout.Token);
        }
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);
        await runtime.CompleteRequestAsync(timeout.Token);

        Assert.Equal(StatusCode.Unavailable, await runtime.WaitForTerminationAsync(timeout.Token));
        WorkerOriginState removed = await WaitForRemovalStateAsync(management, timeout.Token);
        Assert.Equal(WorkerStatus.RemovalRequested, removed.Status);
        Assert.Equal(WorkerLinkState.Unlinked, removed.Link.State);
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
            WorkerStatus.RemovalRequested,
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
            WorkerStatus.Starting,
            (await ReadManagementStateAsync(management, timeout.Token)).Status);
    }

    [Theory]
    [InlineData("Preconfigured", null, 16, false)]
    [InlineData("Preconfigured", 10, 10, false)]
    [InlineData("SpecializationRequired", null, 16, false)]
    [InlineData("SpecializationRequired", 10, 10, false)]
    [InlineData("Preconfigured", null, 16, true)]
    [InlineData("SpecializationRequired", 10, 10, true)]
    public async Task RemovalRequested_IsStickyPollableAndClosesNewAdmission(
        string startupMode, int? maxHttpConcurrency, int expectedConcurrency, bool hasAuthenticationError)
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
        Metadata runtimeHeaders = CreateRuntimeIdentityHeaders("valid");
        await using RelayClient runtime = CreateClient(
            factory.GetFunctionRpcAddress(FunctionRpcRelaySide.Runtime), timeout.Token, runtimeHeaders);
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
        Assert.Equal("https://worker-pod.example:48801/", initialized.WorkerInitResponse.Capabilities["HttpUri"]);
        Assert.Equal("http", initialized.WorkerInitResponse.Capabilities[FunctionGroupNameCapability]);
        Assert.Equal(
            expectedConcurrency.ToString(CultureInfo.InvariantCulture),
            initialized.WorkerInitResponse.Capabilities[MaxHttpConcurrencyCapability]);
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        Assert.Equal(expectedConcurrency, manager.State.MaxHttpConcurrency);
        long sessionId = Assert.IsType<long>(manager.State.SessionId);
        DateTimeOffset startupTime = manager.State.StartupTime;
        while (!manager.State.IsRuntimeInitialized)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }

        manager.SetAuthenticationConfigurationError(hasAuthenticationError);
        WorkerOriginState running = await ReadManagementStateAsync(management, timeout.Token);
        Assert.Equal(WorkerStatus.Running, running.Status);
        Assert.Equal(WorkerLinkState.Linked, running.Link.State);
        Assert.Equal(hasAuthenticationError, running.IsUnhealthy);

        using (HttpResponseMessage before = await http.GetAsync("/before-removal", timeout.Token))
        {
            Assert.Equal(HttpStatusCode.NoContent, before.StatusCode);
        }

        Task<HttpResponseMessage> removalPoll = PollManagementStateAsync(management, running, timeout.Token);
        await WaitForManagementPollAsync(manager, timeout.Token);
        await runtime.CompleteRequestAsync(timeout.Token);
        await Task.WhenAll(
            runtime.WaitForTerminationAsync(timeout.Token),
            worker.WaitForTerminationAsync(timeout.Token));
        using HttpResponseMessage removalResponse = await removalPoll;
        WorkerOriginState removed = await ReadManagementStateAsync(removalResponse, timeout.Token);

        Assert.Equal(WorkerStatus.RemovalRequested, removed.Status);
        Assert.Equal(hasAuthenticationError, removed.IsUnhealthy);
        Assert.Equal(WorkerLinkState.Unlinked, removed.Link.State);
        Assert.Null(removed.Link.Runtime);
        Assert.Equal(WorkerStatus.RemovalRequested, (await ReadManagementStateAsync(management, timeout.Token)).Status);
        WorkerPodState removedState = manager.State;
        Assert.Null(removedState.FunctionAppName);
        Assert.Null(removedState.FunctionGroupName);
        Assert.Null(removedState.HttpProxyEndpoint);
        Assert.Null(removedState.MaxHttpConcurrency);
        Assert.Null(manager.GetWorkerCapabilitySnapshot(sessionId).HttpProxyEndpoint);
        Assert.Equal(startupTime, removedState.StartupTime);
        Assert.False(manager.RequestRemoval());
        Assert.Same(removedState, manager.State);

        using (HttpResponseMessage readiness = await management.GetAsync("/admin/instance/ready", timeout.Token))
        {
            Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
        }
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

    private static async Task<WorkerOriginState> WaitForRemovalStateAsync(
        HttpClient management,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            WorkerOriginState state = await ReadManagementStateAsync(management, cancellationToken);
            if (state.Status == WorkerStatus.RemovalRequested)
            {
                return state;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }
}
