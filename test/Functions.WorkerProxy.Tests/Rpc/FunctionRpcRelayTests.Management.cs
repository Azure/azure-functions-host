// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Linq;
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
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    private const string InfraStatePath = "/admin/infra/instanceState";
    private const string ManagementAssignment = """
        {"startupMode":"SpecializationRequired","functionAppName":"test-app","functionGroupName":"test-group","isAlwaysReady":false,
         "environment":{"B":"private-value","A":"1"},"functionAppDirectory":"/home/site/wwwroot",
         "configLastModifiedTime":1761868800,"contentLastModifiedTime":1761868801,"maxHttpConcurrency":0,"httpProxyEndpoint":"worker-pod:28080"}
        """;

    private const string RuntimeIdentityJson = """
        {"resourceNamespace":"runtime-ns","podName":"runtime-pod",
         "app":{"functionAppName":"test-app","functionAppSiteId":42,"antaresStampName":"test-stamp"}}
        """;

    [Theory]
    [InlineData(true, "Preconfigured", "test-group", 0)]
    [InlineData(false, "Preconfigured", "test-group", 0)]
    [InlineData(true, "SpecializationRequired", "test-group", 10)]
    [InlineData(false, "SpecializationRequired", "test-group", 10)]
    [InlineData(true, "Preconfigured", "http", 16)]
    [InlineData(false, "Preconfigured", "http", 10)]
    [InlineData(true, "SpecializationRequired", "http", 10)]
    [InlineData(false, "SpecializationRequired", "http", 16)]
    public async Task ManagementApis_ObserveNormalStartupAssignmentAndTerminalFailure(
        bool runtimeFirst, string startupMode, string functionGroupName, int maxHttpConcurrency)
    {
        string assignmentBody = ManagementAssignment
            .Replace("SpecializationRequired", startupMode, StringComparison.Ordinal)
            .Replace("test-group", functionGroupName, StringComparison.Ordinal)
            .Replace("\"maxHttpConcurrency\":0", "\"maxHttpConcurrency\":" + maxHttpConcurrency.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        int expectedConcurrency = maxHttpConcurrency;
        const string proxyEndpoint = "https://worker-pod:28080/";
        await using WorkerProxyWebApplicationFactory factory = new();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        WorkerOriginState initial = await ReadManagementStateAsync(management, timeout.Token);
        Assert.Equal(WorkerStatus.Starting, initial.Status);
        Assert.Equal(WorkerLinkState.Unlinked, initial.Link.State);
        using (HttpResponseMessage assignment = await PutManagementJsonAsync(
            management, "/admin/worker/assignment", assignmentBody, timeout.Token))
        {
            Assert.Equal(HttpStatusCode.Created, assignment.StatusCode);
            Assert.Equal("/admin/worker/assignment", assignment.Headers.Location?.OriginalString);
            Assert.Empty(await assignment.Content.ReadAsByteArrayAsync(timeout.Token));
        }

        Assert.Equal(1, manager.State.Revision);
        Assert.Equal(WorkerAssignmentState.Assigned, manager.State.AssignmentState);
        Assert.False(manager.State.IsWorkerReady);
        Assert.Null(manager.State.SessionId);
        Assert.Equal(expectedConcurrency, manager.State.MaxHttpConcurrency);
        DateTimeOffset startupTime = manager.State.StartupTime;
        string replayBody = assignmentBody.Replace(
            "\"B\":\"private-value\",\"A\":\"1\"", "\"A\":\"1\",\"B\":\"private-value\"", StringComparison.Ordinal);
        string conflictingBody = assignmentBody.Replace(
            "\"functionGroupName\":\"" + functionGroupName + "\"",
            "\"functionGroupName\":\"other-group\"",
            StringComparison.Ordinal);
        using (HttpResponseMessage replay = await PutManagementJsonAsync(
            management, "/admin/worker/assignment", replayBody, timeout.Token))
        {
            Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        }

        using (HttpResponseMessage conflict = await PutManagementJsonAsync(
            management, "/admin/worker/assignment", conflictingBody, timeout.Token))
        {
            await AssertManagementErrorAsync(conflict, HttpStatusCode.Conflict, "AssignmentConflict", timeout.Token);
        }

        Assert.Equal(1, manager.State.Revision);
        Metadata runtimeHeaders = CreateRuntimeIdentityHeaders("valid");
        StreamingMessage init = new()
        {
            RequestId = runtimeFirst ? string.Empty : "runtime-init",
            WorkerInitRequest = new() { HostVersion = "test-host", FunctionAppDirectory = "runtime-authoritative-directory" }
        };
        RelayClient? runtime = null;
        try
        {
            if (runtimeFirst)
            {
                runtime = CreateClient(
                    factory.GetFunctionRpcAddress(FunctionRpcRelaySide.Runtime),
                    timeout.Token,
                    runtimeHeaders);
                await runtime.WriteAsync(init, timeout.Token);
                await WaitForAttachmentAsync(factory.Services.GetRequiredService<FunctionRpcRelay>(),
                    FunctionRpcRelaySide.Runtime, timeout.Token);
                Assert.False(manager.State.IsWorkerReady);
            }

            await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
            while (!manager.State.IsWorkerAttached)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
            }
            long beforeStartRevision = manager.State.Revision;
            Assert.Equal(WorkerAssignmentState.Assigned, manager.State.AssignmentState);
            WorkerOriginState beforeStart = await ReadManagementStateAsync(management, timeout.Token);
            Task<HttpResponseMessage> readinessPoll = PollManagementStateAsync(management, beforeStart, timeout.Token);
            await WaitForManagementPollAsync(manager, timeout.Token);
            StreamingMessage start = CreateStartStream();
            await worker.WriteAsync(start, timeout.Token);
            using (HttpResponseMessage changed = await readinessPoll)
            {
                Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
                Assert.True(changed.Headers.CacheControl?.NoStore);
                string body = await changed.Content.ReadAsStringAsync(timeout.Token);
                using JsonDocument assigned = JsonDocument.Parse(body);
                Assert.Equal(beforeStartRevision + 1, assigned.RootElement.GetProperty("revisionId").GetInt64());
                Assert.Equal("Starting", assigned.RootElement.GetProperty("status").GetString());
                Assert.Equal("Unlinked", assigned.RootElement.GetProperty("link").GetProperty("state").GetString());
                Assert.Equal(functionGroupName, assigned.RootElement.GetProperty("functionGroup").GetString());
                Assert.False(assigned.RootElement.GetProperty("isAlwaysReady").GetBoolean());
                Assert.Equal(WorkerAssignmentState.Ready, manager.State.AssignmentState);
                Assert.Equal(startupMode, manager.State.StartupMode.ToString());
                Assert.True(manager.State.IsWorkerReady);
                Assert.Equal(WorkerPodStatus.Running, manager.State.PodStatus);
                Assert.DoesNotContain("private-value", body);
                Assert.DoesNotContain("environment", body);
                Assert.DoesNotContain("runtimePodName", body);
            }

            long readyRevision = manager.State.Revision;
            using (HttpResponseMessage replay = await PutManagementJsonAsync(
                management, "/admin/worker/assignment", replayBody, timeout.Token))
            {
                Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
                Assert.Empty(await replay.Content.ReadAsByteArrayAsync(timeout.Token));
            }

            using (HttpResponseMessage conflict = await PutManagementJsonAsync(
                management, "/admin/worker/assignment", conflictingBody, timeout.Token))
            {
                await AssertManagementErrorAsync(conflict, HttpStatusCode.Conflict, "AssignmentConflict", timeout.Token);
            }

            Assert.Equal(readyRevision, manager.State.Revision);
            if (runtime is null)
            {
                runtime = CreateClient(
                    factory.GetFunctionRpcAddress(FunctionRpcRelaySide.Runtime),
                    timeout.Token,
                    runtimeHeaders);
                await runtime.WriteAsync(init, timeout.Token);
            }
            Assert.Equal(start, await runtime.ReadAsync(timeout.Token));
            Assert.Equal(init, await worker.ReadAsync(timeout.Token));
            StreamingMessage initialized = CreateInitResponse(init.RequestId, "http://localhost:1234/");
            StreamingMessage expected = initialized.Clone();
            expected.WorkerInitResponse.Capabilities["HttpUri"] = proxyEndpoint;
            expected.WorkerInitResponse.Capabilities["FunctionGroupName"] = functionGroupName;
            expected.WorkerInitResponse.Capabilities["IsAlwaysReady"] = bool.FalseString;
            if (string.Equals(functionGroupName, "http", StringComparison.OrdinalIgnoreCase))
            {
                expected.WorkerInitResponse.Capabilities["MaxHttpConcurrency"] = expectedConcurrency.ToString(CultureInfo.InvariantCulture);
            }

            Task<WorkerStatePollResult> initializationPoll =
                manager.WaitForChangeAsync(manager.State.Revision, timeout.Token);
            await worker.WriteAsync(initialized, timeout.Token);
            Assert.Equal(expected, await runtime.ReadAsync(timeout.Token));
            Assert.True((await initializationPoll).State?.IsRuntimeInitialized);
            Assert.Equal(expectedConcurrency, manager.State.MaxHttpConcurrency);
            Assert.Equal(startupTime, manager.State.StartupTime);
            WorkerOriginState running = await ReadManagementStateAsync(management, timeout.Token);
            Assert.Equal(WorkerStatus.Running, running.Status);
            Assert.Equal(WorkerLinkState.Linked, running.Link.State);
            Assert.Equal("runtime-ns", running.Link.Runtime!.ResourceNamespace);
            Assert.Equal("runtime-pod", running.Link.Runtime.PodName);
            Assert.Equal("test-app", running.Link.Runtime.App.FunctionAppName);
            Assert.Equal(42, running.Link.Runtime.App.FunctionAppSiteId);

            Task<HttpResponseMessage> terminationPoll = PollManagementStateAsync(management, running, timeout.Token);
            await WaitForManagementPollAsync(manager, timeout.Token);
            await worker.CompleteRequestAsync(timeout.Token);
            Assert.Equal(StatusCode.Unavailable, await runtime.WaitForTerminationAsync(timeout.Token));
            Assert.Equal(StatusCode.Unavailable, await worker.WaitForTerminationAsync(timeout.Token));
            using (HttpResponseMessage changed = await terminationPoll)
            {
                Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
                Assert.True(changed.Headers.CacheControl?.NoStore);
                using JsonDocument state = JsonDocument.Parse(await changed.Content.ReadAsStringAsync(timeout.Token));
                Assert.Equal(manager.State.Revision, state.RootElement.GetProperty("revisionId").GetInt64());
                Assert.Equal("RemovalRequested", state.RootElement.GetProperty("status").GetString());
                Assert.Equal("Unlinked", state.RootElement.GetProperty("link").GetProperty("state").GetString());
                Assert.Equal(functionGroupName, state.RootElement.GetProperty("functionGroup").GetString());
                Assert.False(state.RootElement.GetProperty("isAlwaysReady").GetBoolean());
                Assert.Equal(startupMode, manager.State.StartupMode.ToString());
                Assert.False(manager.State.IsWorkerReady);
                Assert.Equal(WorkerPodStatus.RemovalRequested, manager.State.PodStatus);
            }

            WorkerOriginState terminated = await ReadManagementStateAsync(management, timeout.Token);
            Assert.Equal(WorkerStatus.RemovalRequested, terminated.Status);
            Assert.Equal(WorkerLinkState.Unlinked, terminated.Link.State);
            Assert.Null(terminated.Link.Runtime);
            Assert.False(terminated.IsUnhealthy);
            using HttpResponseMessage terminalReplay = await PutManagementJsonAsync(
                management, "/admin/worker/assignment", assignmentBody, timeout.Token);
            await AssertManagementErrorAsync(terminalReplay, HttpStatusCode.Conflict, "WorkerTerminated", timeout.Token);
            using HttpResponseMessage terminalConflict = await PutManagementJsonAsync(
                management, "/admin/worker/assignment", conflictingBody, timeout.Token);
            await AssertManagementErrorAsync(terminalConflict, HttpStatusCode.Conflict, "AssignmentConflict", timeout.Token);
            using HttpResponseMessage current = await PollManagementStateAsync(management, beforeStart, timeout.Token);
            Assert.Equal(terminated, await ReadManagementStateAsync(current, timeout.Token));
        }
        finally
        {
            if (runtime is not null)
            {
                await runtime.DisposeAsync();
            }
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public async Task ManagementApis_InvalidWorkerStartupMarksAssignmentUnhealthy(string? workerId)
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpResponseMessage assignment = await PutManagementJsonAsync(
            management, "/admin/worker/assignment", ManagementAssignment, timeout.Token);
        Assert.Equal(HttpStatusCode.Created, assignment.StatusCode);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);

        StreamingMessage first = workerId is null
            ? CreateMessage("not-start-stream")
            : new() { StartStream = new() { WorkerId = workerId } };
        Grpc.Core.RpcException exception = await worker.WriteAndReadRejectionAsync(first, timeout.Token);

        Assert.Equal(StatusCode.Unavailable, exception.StatusCode);
        Assert.False(manager.State.IsWorkerReady);
        Assert.Equal(WorkerAssignmentState.Failed, manager.State.AssignmentState);
        WorkerOriginState failed = await ReadManagementStateAsync(management, timeout.Token);
        Assert.Equal(WorkerStatus.RemovalRequested, failed.Status);
        Assert.True(failed.IsUnhealthy);
        using HttpResponseMessage replay = await PutManagementJsonAsync(
            management, "/admin/worker/assignment", ManagementAssignment, timeout.Token);
        await AssertManagementErrorAsync(replay, HttpStatusCode.Conflict, "WorkerTerminated", timeout.Token);

        while (relay.IsAttached(FunctionRpcRelaySide.Worker))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }

        await using RelayClient replacement = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        Grpc.Core.RpcException replacementFailure = await replacement.WriteAndReadRejectionAsync(
            CreateStartStream("replacement-start"),
            timeout.Token);
        Assert.Equal(StatusCode.Unavailable, replacementFailure.StatusCode);
        Assert.Equal(WorkerAssignmentState.Failed, manager.State.AssignmentState);

        await using RelayClient replacementRuntime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        Grpc.Core.RpcException runtimeFailure = await replacementRuntime.WriteAndReadRejectionAsync(
            CreateMessage("replacement-runtime"),
            timeout.Token);
        Assert.Equal(StatusCode.Unavailable, runtimeFailure.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstanceState_PreInitRuntimeLossRequestsRemovalAndClearsIdentity(bool runtimeIdentified)
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        await AssignWorkerAsync(management, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        StreamingMessage start = CreateStartStream();
        await worker.WriteAsync(start, timeout.Token);
        await WaitForWorkerReadyAsync(manager, timeout.Token);
        await using RelayClient original = CreateClient(
            factory.GetFunctionRpcAddress(FunctionRpcRelaySide.Runtime), timeout.Token,
            runtimeIdentified ? CreateRuntimeIdentityHeaders("valid") : RuntimeAuthenticationTestData.CreateHeaders());
        Assert.Equal(start, await original.ReadAsync(timeout.Token));
        Assert.Equal(runtimeIdentified ? "runtime-pod" : null, manager.State.RuntimeIdentity?.PodName);
        Assert.False(manager.State.IsRuntimeInitialized);
        long attachedRevision = manager.State.Revision;
        Task<WorkerStatePollResult> removalPoll = manager.WaitForChangeAsync(attachedRevision, timeout.Token);

        await original.CompleteRequestAsync(timeout.Token);
        Assert.Equal(StatusCode.Unavailable, await original.WaitForTerminationAsync(timeout.Token));
        Assert.Equal(StatusCode.Unavailable, await worker.WaitForTerminationAsync(timeout.Token));
        await WaitForReleaseAsync(relay, timeout.Token);
        WorkerPodState removed = Assert.IsType<WorkerPodState>((await removalPoll).State);
        Assert.Equal(attachedRevision + 1, removed.Revision);
        Assert.True(removed.RemovalRequested);
        Assert.Null(removed.RuntimeIdentity);
        Assert.False(removed.RuntimeInitialized);
        Assert.Null(removed.SessionId);
        Assert.Null(removed.WorkerId);
        Assert.Equal(WorkerAssignmentState.Unassigned, removed.AssignmentState);
        Assert.False(removed.IsWorkerReady);
        WorkerOriginState checkpoint = await ReadManagementStateAsync(management, timeout.Token);
        Assert.Equal(WorkerStatus.RemovalRequested, checkpoint.Status);
        Assert.Equal(WorkerLinkState.Unlinked, checkpoint.Link.State);
        Assert.Null(checkpoint.Link.Runtime);
        Assert.False(checkpoint.IsUnhealthy);

        await using RelayClient replacement = CreateClient(
            factory.GetFunctionRpcAddress(FunctionRpcRelaySide.Runtime), timeout.Token, CreateRuntimeIdentityHeaders("valid"));
        StreamingMessage init = new() { RequestId = "replacement-init", WorkerInitRequest = new() { HostVersion = "test-host" } };
        Grpc.Core.RpcException rejected = await replacement.WriteAndReadRejectionAsync(init, timeout.Token);
        Assert.Equal(StatusCode.Unavailable, rejected.StatusCode);
        using HttpResponseMessage reassignment = await PutManagementJsonAsync(
            management, "/admin/worker/assignment", ManagementAssignment, timeout.Token);
        await AssertManagementErrorAsync(reassignment, HttpStatusCode.ServiceUnavailable, "WorkerNotReady", timeout.Token);
        Assert.Same(removed, manager.State);
    }

    [Fact]
    public async Task InstanceState_InitializationBeforeAssignment_BecomesLinkedAfterAssignment()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        Metadata runtimeHeaders = CreateRuntimeIdentityHeaders("valid");
        await using RelayClient runtime = CreateClient(
            factory.GetFunctionRpcAddress(FunctionRpcRelaySide.Runtime),
            timeout.Token,
            runtimeHeaders);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        StreamingMessage init = new()
        {
            RequestId = string.Empty,
            WorkerInitRequest = new() { HostVersion = "test-host" }
        };
        StreamingMessage start = CreateStartStream();
        await runtime.WriteAsync(init, timeout.Token);
        await worker.WriteAsync(start, timeout.Token);
        Assert.Equal(start, await runtime.ReadAsync(timeout.Token));
        Assert.Equal(init, await worker.ReadAsync(timeout.Token));

        StreamingMessage initialized = CreateInitResponse(string.Empty, null);
        await worker.WriteAsync(initialized, timeout.Token);
        while (manager.PendingWaiterCount == 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }

        Task<StreamingMessage> forwarded = runtime.ReadAsync(timeout.Token);
        Assert.False(forwarded.IsCompleted);
        Assert.False(manager.State.RuntimeInitialized);
        WorkerOriginState pending = await ReadManagementStateAsync(management, timeout.Token);
        Assert.Equal(WorkerStatus.Starting, pending.Status);
        Assert.Equal(WorkerLinkState.Unlinked, pending.Link.State);

        await AssignWorkerAsync(management, timeout.Token);
        StreamingMessage response = await forwarded;
        Assert.Equal("test-group", response.WorkerInitResponse.Capabilities[FunctionGroupNameCapability]);

        WorkerPodState snapshot = manager.State;
        while (!snapshot.IsRuntimeInitialized)
        {
            snapshot = Assert.IsType<WorkerPodState>((await manager.WaitForChangeAsync(snapshot.Revision, timeout.Token)).State);
        }

        Assert.Equal(WorkerAssignmentState.Ready, manager.State.AssignmentState);
        Assert.True(manager.State.IsRuntimeInitialized);
        WorkerOriginState state = await ReadManagementStateAsync(management, timeout.Token);
        Assert.Equal(WorkerStatus.Running, state.Status);
        Assert.Equal(WorkerLinkState.Linked, state.Link.State);
        Assert.Equal("runtime-pod", state.Link.Runtime?.PodName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("worker-init")]
    public async Task InstanceState_UnmatchedInitializationResponses_RemainUnlinkedUntilCurrentRequestMatches(string requestId)
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(
            factory.GetFunctionRpcAddress(FunctionRpcRelaySide.Runtime),
            timeout.Token,
            CreateRuntimeIdentityHeaders("valid"));
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        StreamingMessage init = new()
        {
            RequestId = requestId,
            WorkerInitRequest = new() { HostVersion = "test-host" }
        };
        StreamingMessage start = CreateStartStream();
        await runtime.WriteAsync(init, timeout.Token);
        await worker.WriteAsync(start, timeout.Token);
        Assert.Equal(start, await runtime.ReadAsync(timeout.Token));
        Assert.Equal(init, await worker.ReadAsync(timeout.Token));

        StreamingMessage unmatched = CreateInitResponse("unmatched-response", null);
        StreamingMessage initialBarrier = CreateMessage("initial-response-written");
        await worker.WriteAllAsync([unmatched, initialBarrier], timeout.Token);
        while (manager.PendingWaiterCount == 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }

        Task<StreamingMessage> forwarded = runtime.ReadAsync(timeout.Token);
        Assert.False(forwarded.IsCompleted);
        await AssignWorkerAsync(management, timeout.Token);
        Assert.Equal("test-group", (await forwarded).WorkerInitResponse.Capabilities[FunctionGroupNameCapability]);
        Assert.Equal(initialBarrier, await runtime.ReadAsync(timeout.Token));
        Assert.Equal(WorkerAssignmentState.Ready, manager.State.AssignmentState);
        Assert.False(manager.State.IsRuntimeInitialized);
        WorkerOriginState state = await ReadManagementStateAsync(management, timeout.Token);
        Assert.Equal(WorkerStatus.Starting, state.Status);
        Assert.Equal(WorkerLinkState.Unlinked, state.Link.State);

        WorkerPodState assigned = manager.State;
        StreamingMessage replayBarrier = CreateMessage("replayed-response-written");
        await worker.WriteAllAsync([unmatched, replayBarrier], timeout.Token);
        await runtime.ReadAsync(timeout.Token);
        Assert.Equal(replayBarrier, await runtime.ReadAsync(timeout.Token));

        Assert.Same(assigned, manager.State);
        Assert.False(manager.State.IsRuntimeInitialized);
        Assert.Equal(state, await ReadManagementStateAsync(management, timeout.Token));

        StreamingMessage nextInit = new()
        {
            RequestId = "next-init",
            WorkerInitRequest = new() { HostVersion = "test-host" }
        };
        await runtime.WriteAsync(nextInit, timeout.Token);
        Assert.Equal(nextInit, await worker.ReadAsync(timeout.Token));

        StreamingMessage staleBarrier = CreateMessage("stale-response-written");
        await worker.WriteAllAsync([CreateInitResponse(requestId, null), staleBarrier], timeout.Token);
        await runtime.ReadAsync(timeout.Token);
        Assert.Equal(staleBarrier, await runtime.ReadAsync(timeout.Token));
        Assert.Same(assigned, manager.State);

        Task<WorkerStatePollResult> initializationPoll =
            manager.WaitForChangeAsync(manager.State.Revision, timeout.Token);
        await worker.WriteAsync(CreateInitResponse(nextInit.RequestId, null), timeout.Token);
        await runtime.ReadAsync(timeout.Token);

        Assert.True((await initializationPoll).State?.IsRuntimeInitialized);
        WorkerOriginState running = await ReadManagementStateAsync(management, timeout.Token);
        Assert.Equal(WorkerStatus.Running, running.Status);
        Assert.Equal(WorkerLinkState.Linked, running.Link.State);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("null")]
    [InlineData("missing")]
    [InlineData("blank")]
    [InlineData("invalid-app")]
    [InlineData("duplicate")]
    public async Task EventStream_InvalidRuntimeIdentity_ReturnsInvalidArgumentWithoutChangingState(string testCase)
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using CancellationTokenSource timeout = new(TestTimeout);
        WorkerPodState initial = manager.State;
        Metadata headers = CreateRuntimeIdentityHeaders(testCase);
        await using RelayClient runtime = CreateClient(
            factory.GetFunctionRpcAddress(FunctionRpcRelaySide.Runtime),
            timeout.Token,
            headers);

        Grpc.Core.RpcException exception = await runtime.WriteAndReadRejectionAsync(
            CreateMessage("invalid-runtime"),
            timeout.Token);

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
        Assert.Equal(initial, manager.State);
        Assert.False(relay.IsAttached(FunctionRpcRelaySide.Runtime));
        Assert.Null(relay.LastTerminalState);
    }

    [Fact]
    public async Task EventStream_RuntimeFirstAppMismatch_ReturnsUnavailableWithoutChangingState()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        await AssignWorkerAsync(management, timeout.Token);
        WorkerPodState assigned = manager.State;
        await using RelayClient runtime = CreateClient(
            factory.GetFunctionRpcAddress(FunctionRpcRelaySide.Runtime),
            timeout.Token,
            CreateRuntimeIdentityHeaders("app-mismatch"));

        Grpc.Core.RpcException exception = await runtime.WriteAndReadRejectionAsync(
            CreateMessage("wrong-runtime"),
            timeout.Token);

        Assert.Equal(StatusCode.Unavailable, exception.StatusCode);
        Assert.Equal(assigned, manager.State);
        Assert.False(relay.IsAttached(FunctionRpcRelaySide.Runtime));
        Assert.Null(relay.LastTerminalState);
    }

    [Fact]
    public async Task EventStream_WorkerFirstAppMismatch_DoesNotDisplaceWorker()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        await AssignWorkerAsync(management, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        StreamingMessage start = CreateStartStream();
        await worker.WriteAsync(start, timeout.Token);
        await WaitForWorkerReadyAsync(manager, timeout.Token);
        WorkerPodState ready = manager.State;
        await using RelayClient rejectedRuntime = CreateClient(
            factory.GetFunctionRpcAddress(FunctionRpcRelaySide.Runtime),
            timeout.Token,
            CreateRuntimeIdentityHeaders("app-mismatch"));

        Grpc.Core.RpcException exception = await rejectedRuntime.WriteAndReadRejectionAsync(
            CreateMessage("wrong-runtime"),
            timeout.Token);

        Assert.Equal(StatusCode.Unavailable, exception.StatusCode);
        Assert.Equal(ready, manager.State);
        Assert.True(relay.IsAttached(FunctionRpcRelaySide.Worker));
        Assert.False(relay.IsAttached(FunctionRpcRelaySide.Runtime));

        await using RelayClient runtime = CreateClient(
            factory.GetFunctionRpcAddress(FunctionRpcRelaySide.Runtime),
            timeout.Token,
            CreateRuntimeIdentityHeaders("valid"));
        StreamingMessage runtimeMessage = CreateMessage("valid-runtime");
        await runtime.WriteAsync(runtimeMessage, timeout.Token);
        Assert.Equal(runtimeMessage, await worker.ReadAsync(timeout.Token));
        Assert.Equal(start, await runtime.ReadAsync(timeout.Token));
    }

    [Fact]
    public async Task InstanceState_Startup_PublishesFlattenedCheckpoint()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);

        using HttpResponseMessage placeholderResponse = await PollManagementStateAsync(
            management,
            previousState: null,
            timeout.Token);
        Assert.Equal(HttpStatusCode.OK, placeholderResponse.StatusCode);
        Assert.True(placeholderResponse.Headers.CacheControl?.NoStore);
        string placeholderJson = await placeholderResponse.Content.ReadAsStringAsync(timeout.Token);
        using JsonDocument wireState = JsonDocument.Parse(placeholderJson);
        JsonElement wireRoot = wireState.RootElement;
        AssertProperties(
            wireRoot,
            "functionsContainerType",
            "self",
            "isAlwaysReady",
            "functionGroup",
            "configLastModifiedTime",
            "contentLastModifiedTime",
            "startupTime",
            "revisionId",
            "status",
            "link",
            "isUnhealthy");
        Assert.Equal(JsonValueKind.Null, wireRoot.GetProperty("functionGroup").ValueKind);
        Assert.False(wireRoot.TryGetProperty("staticState", out _));
        Assert.False(wireRoot.TryGetProperty("dynamicState", out _));
        JsonElement wireSelf = wireRoot.GetProperty("self");
        AssertProperties(
            wireSelf,
            "resourceNamespace",
            "podName",
            "app");
        AssertProperties(
            wireSelf.GetProperty("app"),
            "functionAppName",
            "functionAppSiteId",
            "antaresStampName");
        JsonElement wireLink = wireRoot.GetProperty("link");
        AssertProperties(wireLink, "state");
        Assert.Equal("Unlinked", wireLink.GetProperty("state").GetString());

        WorkerOriginState placeholder = Assert.IsType<WorkerOriginState>(
            JsonSerializer.Deserialize(placeholderJson, WorkerProxyJsonContext.Default.WorkerOriginState));
        Assert.Equal("FunctionsSplitWorkerProxy", wireRoot.GetProperty("functionsContainerType").GetString());
        Assert.Equal(WorkerOriginState.ContainerType, placeholder.FunctionsContainerType);
        Assert.Equal("unknown", placeholder.Self.ResourceNamespace);
        Assert.Equal("test-worker-pod", placeholder.Self.PodName);
        Assert.Equal(new FunctionAppIdentity("unknown", 0, "unknown"), placeholder.Self.App);
        Assert.False(placeholder.IsAlwaysReady);
        Assert.Null(placeholder.FunctionGroup);
        Assert.Equal(0, placeholder.ConfigLastModifiedTime);
        Assert.Equal(0, placeholder.ContentLastModifiedTime);
        Assert.NotEqual(default(DateTimeOffset), placeholder.StartupTime);
        Assert.Equal(0, placeholder.RevisionId);
        Assert.Equal(WorkerStatus.Starting, placeholder.Status);
        Assert.Equal(new WorkerLink(WorkerLinkState.Unlinked), placeholder.Link);
        Assert.False(placeholder.IsUnhealthy);

        // Verify Legion NNA echo compatibility when null properties (e.g. functionGroup at Revision 0) are omitted.
        System.Text.Json.Nodes.JsonObject nnaEcho = System.Text.Json.Nodes.JsonNode.Parse(placeholderJson)!.AsObject();
        nnaEcho.Remove("functionGroup");
        nnaEcho["revisionId"] = 1;
        using StringContent echoContent = new(nnaEcho.ToJsonString(), Encoding.UTF8, "application/json");
        using HttpResponseMessage echoResponse = await management.PostAsync(InfraStatePath, echoContent, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, echoResponse.StatusCode);
    }

    [Fact]
    public async Task InstanceState_Assignment_UpdatesLongPoll()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        WorkerOriginState placeholder = await ReadManagementStateAsync(management, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await worker.WriteAsync(CreateStartStream(), timeout.Token);
        await WaitForWorkerReadyAsync(manager, timeout.Token);

        WorkerOriginState preAssignment = await ReadManagementStateAsync(management, timeout.Token);
        Assert.Equal(2, preAssignment.RevisionId);
        Assert.Equal(WorkerStatus.Starting, preAssignment.Status);
        Assert.Equal(new WorkerLink(WorkerLinkState.Unlinked), preAssignment.Link);

        Task<HttpResponseMessage> assignmentPoll = PollManagementStateAsync(
            management,
            preAssignment,
            timeout.Token);
        await WaitForManagementPollAsync(manager, timeout.Token);
        Assert.False(assignmentPoll.IsCompleted);
        string legacyAssignment = ManagementAssignment
            .Replace("\"functionGroupName\":\"test-group\"", "\"functionGroupName\":\"deployment:validation\"", StringComparison.Ordinal)
            .Replace("\"configLastModifiedTime\":1761868800", "\"configLastModifiedTime\":111", StringComparison.Ordinal)
            .Replace("\"contentLastModifiedTime\":1761868801", "\"contentLastModifiedTime\":222", StringComparison.Ordinal);
        using (HttpResponseMessage assignmentResponse = await PutManagementJsonAsync(
            management,
            "/admin/worker/assignment",
            legacyAssignment,
            timeout.Token))
        {
            Assert.Equal(HttpStatusCode.Created, assignmentResponse.StatusCode);
        }

        using HttpResponseMessage assignedResponse = await assignmentPoll;
        WorkerOriginState assigned = await ReadManagementStateAsync(assignedResponse, timeout.Token);
        Assert.Equal(placeholder.Self, assigned.Self);
        Assert.False(assigned.IsAlwaysReady);
        Assert.Equal("deployment:validation", assigned.FunctionGroup);
        Assert.Equal(111, assigned.ConfigLastModifiedTime);
        Assert.Equal(222, assigned.ContentLastModifiedTime);
        Assert.Equal(placeholder.StartupTime, assigned.StartupTime);
        Assert.Equal(3, assigned.RevisionId);
        Assert.Equal(WorkerStatus.Starting, assigned.Status);
        Assert.Equal(new WorkerLink(WorkerLinkState.Unlinked), assigned.Link);
        Assert.False(assigned.IsUnhealthy);

        using HttpResponseMessage differentStateResponse = await PollManagementStateAsync(
            management,
            assigned with { FunctionGroup = "stale-cached-group" },
            timeout.Token);
        Assert.Equal(assigned, await ReadManagementStateAsync(differentStateResponse, timeout.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InfraInstanceState_RuntimeAttachAndWorkerTermination_KeepsDeferredLinkDefaults(bool runtimeCloses)
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await worker.WriteAsync(CreateStartStream(), timeout.Token);
        await WaitForWorkerReadyAsync(manager, timeout.Token);
        await AssignWorkerAsync(management, timeout.Token);
        WorkerOriginState assigned = await ReadManagementStateAsync(management, timeout.Token);

        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await runtime.WriteAsync(CreateMessage("runtime-attach"), timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);

        Assert.Equal(assigned.RevisionId, manager.State.Revision);
        Assert.Equal(assigned, await ReadManagementStateAsync(management, timeout.Token));
        Assert.Equal(WorkerStatus.Starting, assigned.Status);
        Assert.Equal(new WorkerLink(WorkerLinkState.Unlinked), assigned.Link);

        Assert.Equal(CreateMessage("runtime-attach"), await worker.ReadAsync(timeout.Token));
        Task<HttpResponseMessage> terminationPoll = PollManagementStateAsync(
            management,
            assigned,
            timeout.Token);
        await WaitForManagementPollAsync(manager, timeout.Token);
        Assert.False(terminationPoll.IsCompleted);
        await (runtimeCloses ? runtime : worker).CompleteRequestAsync(timeout.Token);
        await Task.WhenAll(
            runtime.WaitForTerminationAsync(timeout.Token),
            worker.WaitForTerminationAsync(timeout.Token));

        using HttpResponseMessage terminatedResponse = await terminationPoll;
        WorkerOriginState terminated = await ReadManagementStateAsync(terminatedResponse, timeout.Token);
        Assert.Equal(assigned.Self, terminated.Self);
        Assert.Equal(assigned.IsAlwaysReady, terminated.IsAlwaysReady);
        Assert.Equal(assigned.FunctionGroup, terminated.FunctionGroup);
        Assert.Equal(1761868800, terminated.ConfigLastModifiedTime);
        Assert.Equal(1761868801, terminated.ContentLastModifiedTime);
        Assert.Equal(assigned.StartupTime, terminated.StartupTime);
        Assert.Equal(assigned.RevisionId + 1, terminated.RevisionId);
        Assert.Equal(
            runtimeCloses ? WorkerAssignmentState.Unassigned : WorkerAssignmentState.Terminated,
            manager.State.AssignmentState);
        Assert.Equal(runtimeCloses, manager.State.RemovalRequested);
        Assert.Equal(WorkerStatus.RemovalRequested, terminated.Status);
        Assert.Equal(new WorkerLink(WorkerLinkState.Unlinked), terminated.Link);
        Assert.False(terminated.IsUnhealthy);
    }

    private static async Task<HttpResponseMessage> PutManagementJsonAsync(
        HttpClient client, string path, string body, CancellationToken cancellationToken)
    {
        using StringContent content = new(body, Encoding.UTF8, "application/json");

        return await client.PutAsync(path, content, cancellationToken);
    }

    private static async Task AssignWorkerAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await PutManagementJsonAsync(
            client,
            "/admin/worker/assignment",
            ManagementAssignment,
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static Metadata CreateRuntimeIdentityHeaders(string testCase)
    {
        const string valid = RuntimeIdentityJson;
        string identity = testCase switch
        {
            "malformed" => "{",
            "null" => "null",
            "missing" => """{"podName":"runtime-pod","app":{"functionAppName":"test-app","functionAppSiteId":42,"antaresStampName":"test-stamp"}}""",
            "blank" => """{"resourceNamespace":" ","podName":"runtime-pod","app":{"functionAppName":"test-app","functionAppSiteId":42,"antaresStampName":"test-stamp"}}""",
            "invalid-app" => """{"resourceNamespace":"runtime-ns","podName":"runtime-pod","app":{"functionAppName":"test-app","functionAppSiteId":0,"antaresStampName":"test-stamp"}}""",
            "app-mismatch" => """{"resourceNamespace":"runtime-ns","podName":"runtime-pod","app":{"functionAppName":"other-app","functionAppSiteId":42,"antaresStampName":"test-stamp"}}""",
            "valid" => valid,
            "duplicate" => valid,
            _ => throw new ArgumentOutOfRangeException(nameof(testCase), testCase, "Unknown Runtime identity case.")
        };
        Metadata headers = RuntimeAuthenticationTestData.CreateHeaders();
        headers.Add("x-ms-functions-runtime-identity-bin", Encoding.UTF8.GetBytes(identity));
        if (string.Equals(testCase, "duplicate", StringComparison.Ordinal))
        {
            headers.Add("x-ms-functions-runtime-identity-bin", Encoding.UTF8.GetBytes(valid));
        }

        return headers;
    }

    private static async Task<WorkerOriginState> ReadManagementStateAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await PollManagementStateAsync(
            client,
            previousState: null,
            cancellationToken);
        return await ReadManagementStateAsync(response, cancellationToken);
    }

    private static async Task<WorkerOriginState> ReadManagementStateAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        WorkerOriginState? state = JsonSerializer.Deserialize(
            await response.Content.ReadAsStringAsync(cancellationToken),
            WorkerProxyJsonContext.Default.WorkerOriginState);
        return Assert.IsType<WorkerOriginState>(state);
    }

    private static async Task<HttpResponseMessage> PollManagementStateAsync(
        HttpClient client,
        WorkerOriginState? previousState,
        CancellationToken cancellationToken)
    {
        using HttpContent? content = previousState is null
            ? null
            : new StringContent(
                JsonSerializer.Serialize(previousState, WorkerProxyJsonContext.Default.WorkerOriginState),
                Encoding.UTF8,
                "application/json");
        return await client.PostAsync(InfraStatePath, content, cancellationToken);
    }

    private static async Task WaitForWorkerReadyAsync(
        WorkerPodStateManager manager,
        CancellationToken cancellationToken)
    {
        while (!manager.State.IsWorkerReady)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }

    private static async Task AssertManagementErrorAsync(
        HttpResponseMessage response, HttpStatusCode statusCode, string code, CancellationToken cancellationToken)
    {
        Assert.Equal(statusCode, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("error", Assert.Single(json.RootElement.EnumerateObject()).Name);
        Assert.Equal(code, json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private static async Task WaitForManagementPollAsync(WorkerPodStateManager manager, CancellationToken cancellationToken)
    {
        while (manager.PendingWaiterCount == 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }

    private static void AssertProperties(JsonElement element, params string[] names)
    {
        Assert.Equal(
            names.OrderBy(name => name, StringComparer.Ordinal),
            element.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
    }
}
