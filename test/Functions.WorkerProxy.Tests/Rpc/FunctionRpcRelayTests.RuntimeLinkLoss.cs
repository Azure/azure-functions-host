// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Management;
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    private const string InfraStatePath = "/admin/infra/instanceState";
    private const string RuntimeIdentityJson = """
        {"resourceNamespace":"runtime-ns","podName":"runtime-pod",
         "app":{"functionAppName":"test-app","functionAppSiteId":42,"antaresStampName":"test-stamp"}}
        """;

    [Fact]
    public async Task RuntimeEventStream_EofBeforeWorkerInitializationRequestsRemoval()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpClient management = factory.CreateWorkerProxyClient();
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        SplitWorkerOriginState initial = await ReadSplitManagementStateAsync(management, timeout.Token);

        await runtime.WriteAsync(CreateMessage("runtime-admitted"), timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);
        await runtime.CompleteRequestAsync(timeout.Token);

        Assert.Equal(StatusCode.Unavailable, await runtime.WaitForTerminationAsync(timeout.Token));
        SplitWorkerOriginState removed = await WaitForRemovalStateAsync(management, timeout.Token);
        Assert.Equal(SplitWorkerStatus.RemovalRequested, removed.Status);
        Assert.Equal(SplitWorkerLinkState.Unlinked, removed.Link.State);
        Assert.Equal(initial.RevisionId + 1, removed.RevisionId);
        Assert.Equal(initial.StartupTime, removed.StartupTime);
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
    public async Task RuntimeEventStream_WriteFailureRequestsRemoval()
    {
        WorkerPodStateManager manager = CreatePodStateManager();
        await using FunctionRpcRelay relay = CreateInProcessRelay(manager);
        using CancellationTokenSource timeout = new(TestTimeout);
        IOException expectedException = new("Injected Runtime write failure.");
        Mock<IServerStreamWriter<StreamingMessage>> writer = new(MockBehavior.Strict);
        writer.Setup(instance => instance.WriteAsync(It.IsAny<StreamingMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        Task<FunctionRpcRelayTerminalState> runtimeTask = relay.AttachAsync(
            FunctionRpcRelaySide.Runtime, new BlockingStreamReader(), writer.Object, timeout.Token);
        Task<FunctionRpcRelayTerminalState> workerTask = relay.AttachAsync(
            FunctionRpcRelaySide.Worker, new SingleMessageThenBlockStreamReader(CreateStartStream()),
            new TestServerStreamWriter(), timeout.Token);

        FunctionRpcRelayTerminalState[] states = await Task.WhenAll(runtimeTask, workerTask).WaitAsync(timeout.Token);
        Assert.All(states, state =>
        {
            Assert.Equal(FunctionRpcRelaySide.Runtime, state.Side);
            Assert.Equal(FunctionRpcRelayTerminationReason.Faulted, state.Reason);
            Assert.Same(expectedException, state.Exception);
        });
        Assert.True(manager.State.RemovalRequested);
        Assert.False(manager.State.IsWorkerReady);
        writer.Verify(instance => instance.WriteAsync(It.IsAny<StreamingMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RuntimeEventStream_ReadFailureRequestsRemoval()
    {
        WorkerPodStateManager manager = CreatePodStateManager();
        await using FunctionRpcRelay relay = CreateInProcessRelay(manager);
        using CancellationTokenSource timeout = new(TestTimeout);
        IOException expectedException = new("Injected Runtime read failure.");
        TaskCompletionSource releaseFault = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<FunctionRpcRelayTerminalState> runtimeTask = relay.AttachAsync(
            FunctionRpcRelaySide.Runtime, new GatedFaultingStreamReader(releaseFault.Task, expectedException),
            new TestServerStreamWriter(), timeout.Token);

        releaseFault.SetResult();
        FunctionRpcRelayTerminalState terminal = await runtimeTask.WaitAsync(timeout.Token);

        Assert.Equal(FunctionRpcRelaySide.Runtime, terminal.Side);
        Assert.Equal(FunctionRpcRelayTerminationReason.Faulted, terminal.Reason);
        Assert.Same(expectedException, terminal.Exception);
        Assert.True(manager.State.RemovalRequested);
        Assert.False(manager.State.IsWorkerReady);
    }

    [Theory]
    [InlineData(true, "init-request", "different-response", true)]
    [InlineData(true, null, "unsolicited-response", true)]
    [InlineData(true, "init-request", "init-request", false)]
    [InlineData(false, "init-request", "init-request", true)]
    public async Task RuntimeEventStream_IncompleteInitializationRemainsUnlinkedOnRemoval(
        bool includeIdentity, string? requestId, string responseRequestId, bool success)
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpClient management = factory.CreateWorkerProxyClient();
        Metadata? headers = includeIdentity
            ? new Metadata { { "x-ms-functions-runtime-identity-bin", Encoding.UTF8.GetBytes(RuntimeIdentityJson) } }
            : null;
        await using RelayClient runtime = CreateClient(
            factory.GetFunctionRpcAddress(FunctionRpcRelaySide.Runtime), timeout.Token, headers);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await worker.WriteAsync(CreateStartStream(), timeout.Token);
        Assert.NotNull((await runtime.ReadAsync(timeout.Token)).StartStream);
        using (HttpResponseMessage assignment = await PutManagementJsonAsync(
            management, "/admin/worker/assignment", ManagementAssignment, timeout.Token))
        {
            Assert.Equal(HttpStatusCode.Created, assignment.StatusCode);
        }

        if (requestId is not null)
        {
            StreamingMessage request = new() { RequestId = requestId, WorkerInitRequest = new() };
            await runtime.WriteAsync(request, timeout.Token);
            Assert.Equal(request, await worker.ReadAsync(timeout.Token));
        }

        StreamingMessage response = CreateInitResponse(responseRequestId, httpUri: null);
        response.WorkerInitResponse.Result.Status = success ? StatusResult.Types.Status.Success : StatusResult.Types.Status.Failure;
        await worker.WriteAsync(response, timeout.Token);
        Assert.NotNull((await runtime.ReadAsync(timeout.Token)).WorkerInitResponse);
        StreamingMessage marker = CreateMessage("after-init-response");
        await worker.WriteAsync(marker, timeout.Token);
        Assert.Equal(marker, await runtime.ReadAsync(timeout.Token));
        SplitWorkerOriginState starting = await ReadSplitManagementStateAsync(management, timeout.Token);
        Assert.Equal(SplitWorkerStatus.Starting, starting.Status);
        Assert.Equal(SplitWorkerLinkState.Unlinked, starting.Link.State);
        Assert.Null(starting.Link.Runtime);

        await runtime.CompleteRequestAsync(timeout.Token);
        SplitWorkerOriginState removed = await WaitForRemovalStateAsync(management, timeout.Token);
        Assert.Equal(SplitWorkerLinkState.Unlinked, removed.Link.State);
        Assert.Null(removed.Link.Runtime);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("not-json")]
    [InlineData("""{"resourceNamespace":"ns","podName":"pod","app":null}""")]
    public async Task RuntimeIdentity_InvalidMetadataRejectsAdmissionWithoutRemoval(string identityJson)
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        using CancellationTokenSource timeout = new(TestTimeout);
        Metadata headers = new()
        {
            { "x-ms-functions-runtime-identity-bin", Encoding.UTF8.GetBytes(identityJson) }
        };
        await using RelayClient runtime = CreateClient(
            factory.GetFunctionRpcAddress(FunctionRpcRelaySide.Runtime), timeout.Token, headers);

        Grpc.Core.RpcException rejected = await runtime.WriteAndReadRejectionAsync(
            CreateMessage("rejected"), timeout.Token);

        Assert.Equal(StatusCode.InvalidArgument, rejected.StatusCode);
        WorkerPodState state = factory.Services.GetRequiredService<WorkerPodStateManager>().State;
        Assert.Equal(0, state.Revision);
        Assert.Null(state.RuntimeIdentity);
        Assert.False(state.RemovalRequested);
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
        Assert.False(factory.Services.GetRequiredService<WorkerPodStateManager>().State.RemovalRequested);
    }

    [Theory]
    [InlineData(false, false, "worker-init")]
    [InlineData(false, false, "")]
    [InlineData(false, true, "worker-init")]
    [InlineData(true, false, "worker-init")]
    [InlineData(true, true, "worker-init")]
    public async Task RemovalRequested_IsStickyPollableAndClosesNewAdmission(
        bool terminateWorker, bool cancelPeer, string initializationRequestId)
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
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using CancellationTokenSource timeout = new(TestTimeout);
        using CancellationTokenSource peerCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        using HttpClient management = factory.CreateWorkerProxyClient();
        using HttpClient http = factory.CreateHttpForwardingClient();
        await using RelayClient worker = CreateClient(
            factory, FunctionRpcRelaySide.Worker, terminateWorker ? peerCancellation.Token : timeout.Token);
        Metadata runtimeHeaders = new()
        {
            { "x-ms-functions-runtime-identity-bin", Encoding.UTF8.GetBytes(RuntimeIdentityJson) }
        };
        await using RelayClient runtime = CreateClient(
            factory.GetFunctionRpcAddress(FunctionRpcRelaySide.Runtime),
            terminateWorker ? timeout.Token : peerCancellation.Token, runtimeHeaders);
        await InitializeAndAssignWorkerAsync(worker, runtime, management, timeout.Token, initializationRequestId);
        while (true)
        {
            WorkerPodState snapshot = manager.State;
            if (snapshot.IsRuntimeInitialized)
            {
                break;
            }

            await manager.WaitForChangeAsync(snapshot.Revision, timeout.Token);
        }

        using (HttpResponseMessage before = await http.GetAsync("/before-removal", timeout.Token))
        {
            Assert.Equal(HttpStatusCode.NoContent, before.StatusCode);
        }

        SplitWorkerOriginState assigned = await ReadSplitManagementStateAsync(management, timeout.Token);
        Assert.Equal(SplitWorkerStatus.Running, assigned.Status);
        Assert.Equal(SplitWorkerLinkState.Linked, assigned.Link.State);
        SplitPodIdentity runtimeIdentity = Assert.IsType<SplitPodIdentity>(assigned.Link.Runtime);
        Assert.Equal("runtime-pod", runtimeIdentity.PodName);
        Assert.Equal("runtime-ns", runtimeIdentity.ResourceNamespace);
        Assert.Equal("test-app", runtimeIdentity.App.FunctionAppName);
        Assert.Equal(42, runtimeIdentity.App.FunctionAppSiteId);
        Task<HttpResponseMessage> pendingPoll = PollSplitManagementStateAsync(management, assigned, timeout.Token);
        await WaitForManagementPollAsync(manager, timeout.Token);
        if (cancelPeer)
        {
            peerCancellation.Cancel();
        }
        else
        {
            await (terminateWorker ? worker : runtime).CompleteRequestAsync(timeout.Token);
        }

        await Task.WhenAll(
            runtime.WaitForTerminationAsync(timeout.Token),
            worker.WaitForTerminationAsync(timeout.Token));
        using HttpResponseMessage changed = await pendingPoll;
        SplitWorkerOriginState removed = await ReadSplitManagementStateAsync(changed, timeout.Token);

        Assert.Equal(SplitWorkerStatus.RemovalRequested, removed.Status);
        Assert.Equal(assigned.RevisionId + 1, removed.RevisionId);
        Assert.Null(removed.FunctionGroup);
        Assert.Equal(assigned.StartupTime, removed.StartupTime);
        Assert.Equal(assigned.Link, removed.Link);
        Assert.True(removed.IsUnhealthy);
        Assert.Equal(removed, await ReadSplitManagementStateAsync(management, timeout.Token));

        WorkerPodState state = manager.State;
        Assert.Null(state.SessionId);
        Assert.Null(state.WorkerId);
        Assert.Null(state.StartupMode);
        Assert.Null(state.FunctionAppName);
        Assert.Null(state.FunctionGroupName);
        Assert.Null(state.IsAlwaysReady);
        Assert.False(state.IsWorkerAttached);
        Assert.False(state.IsWorkerReady);
        Assert.False(state.IsRuntimeInitialized);
        Assert.True(state.RuntimeInitialized);
        Assert.Equal(runtimeIdentity, state.RuntimeIdentity);
        Assert.Equal(WorkerAssignmentState.Unassigned, state.AssignmentState);
        Assert.False(manager.RequestRemoval());
        Assert.False(manager.OnSessionTerminated(1));
        Assert.Same(state, manager.State);

        await AssertReadinessAsync(management, "/admin/instance/ready", HttpStatusCode.OK, timeout.Token);
        await AssertReadinessAsync(management, "/admin/worker/ready", HttpStatusCode.ServiceUnavailable, timeout.Token);
        using (HttpResponseMessage assignment = await PutManagementJsonAsync(
            management, "/admin/worker/assignment", ManagementAssignment, timeout.Token))
        {
            await AssertManagementErrorAsync(assignment, HttpStatusCode.ServiceUnavailable, "WorkerNotReady", timeout.Token);
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

        Assert.Equal(removed, await ReadSplitManagementStateAsync(management, timeout.Token));
    }

    private static async Task InitializeAndAssignWorkerAsync(
        RelayClient worker,
        RelayClient runtime,
        HttpClient management,
        CancellationToken cancellationToken,
        string initializationRequestId = "worker-init")
    {
        StreamingMessage start = CreateStartStream();
        await worker.WriteAsync(start, cancellationToken);
        Assert.Equal(start, await runtime.ReadAsync(cancellationToken));
        using (HttpResponseMessage response = await PutManagementJsonAsync(
            management, "/admin/worker/assignment", ManagementAssignment, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        StreamingMessage request = new()
        {
            RequestId = initializationRequestId,
            WorkerInitRequest = new() { HostVersion = "test-host" }
        };
        await runtime.WriteAsync(request, cancellationToken);
        Assert.Equal(request, await worker.ReadAsync(cancellationToken));
        await worker.WriteAsync(CreateInitResponse(request.RequestId, httpUri: null), cancellationToken);
        Assert.NotNull((await runtime.ReadAsync(cancellationToken)).WorkerInitResponse);
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

    private static async Task<SplitWorkerOriginState> ReadSplitManagementStateAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await PollSplitManagementStateAsync(
            client, previousState: null, cancellationToken);
        return await ReadSplitManagementStateAsync(response, cancellationToken);
    }

    private static async Task<SplitWorkerOriginState> ReadSplitManagementStateAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        SplitWorkerOriginState? state = JsonSerializer.Deserialize(
            await response.Content.ReadAsStringAsync(cancellationToken),
            WorkerProxyJsonContext.Default.SplitWorkerOriginState);
        return Assert.IsType<SplitWorkerOriginState>(state);
    }

    private static async Task<HttpResponseMessage> PollSplitManagementStateAsync(
        HttpClient client,
        SplitWorkerOriginState? previousState,
        CancellationToken cancellationToken)
    {
        using HttpContent? content = previousState is null
            ? null
            : new StringContent(
                JsonSerializer.Serialize(previousState, WorkerProxyJsonContext.Default.SplitWorkerOriginState),
                Encoding.UTF8,
                "application/json");
        return await client.PostAsync(InfraStatePath, content, cancellationToken);
    }
}
