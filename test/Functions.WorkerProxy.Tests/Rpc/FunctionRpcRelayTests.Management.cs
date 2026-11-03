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
         "environment":{"B":"private-value","A":"1"},"functionAppDirectory":"/home/site/wwwroot"}
        """;

    private const string RuntimeIdentityJson = """
        {"resourceNamespace":"runtime-ns","podName":"runtime-pod",
         "app":{"functionAppName":"test-app","functionAppSiteId":42,"antaresStampName":"test-stamp"}}
        """;

    [Theory]
    [InlineData(true, "Preconfigured")]
    [InlineData(false, "Preconfigured")]
    [InlineData(true, "SpecializationRequired")]
    [InlineData(false, "SpecializationRequired")]
    public async Task ManagementApis_ObserveNormalStartupAssignmentAndTerminalFailure(bool runtimeFirst, string startupMode)
    {
        string assignmentBody = ManagementAssignment.Replace("SpecializationRequired", startupMode, StringComparison.Ordinal);
        const string proxyEndpoint = "http://worker-pod:28080/";
        await using WorkerProxyWebApplicationFactory factory = CreateHttpCapabilityFactory(proxyEndpoint);
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        await AssertReadinessAsync(management, "/admin/instance/ready", HttpStatusCode.OK, timeout.Token);
        await AssertReadinessAsync(management, "/admin/worker/ready", HttpStatusCode.ServiceUnavailable, timeout.Token);
        using (HttpResponseMessage assignment = await PutManagementJsonAsync(
            management, "/admin/worker/assignment", assignmentBody, timeout.Token))
        {
            Assert.Equal(HttpStatusCode.Created, assignment.StatusCode);
            Assert.Equal("/admin/worker/assignment", assignment.Headers.Location?.OriginalString);
            Assert.Empty(await assignment.Content.ReadAsByteArrayAsync(timeout.Token));
        }

        Assert.Equal(1, manager.State.Revision);
        Assert.Equal(WorkerAssignmentState.Assigned, manager.State.AssignmentState);
        Metadata runtimeHeaders = new()
        {
            { "x-ms-functions-runtime-identity-bin", Encoding.UTF8.GetBytes(RuntimeIdentityJson) }
        };
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
                await AssertReadinessAsync(management, "/admin/worker/ready", HttpStatusCode.ServiceUnavailable, timeout.Token);
            }

            await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
            while (!manager.State.IsWorkerAttached)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
            }

            long beforeStartRevision = manager.State.Revision;
            Assert.Equal(WorkerAssignmentState.Assigned, manager.State.AssignmentState);
            Task<HttpResponseMessage> readinessPoll = management.GetAsync(
                "/admin/worker/state?lastKnownRevision=" +
                    beforeStartRevision.ToString(CultureInfo.InvariantCulture),
                timeout.Token);
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
                JsonElement pod = assigned.RootElement.GetProperty("workerPodState");
                Assert.Equal("ReadyForRequest", pod.GetProperty("podStatus").GetString());
                Assert.Equal(startupMode, pod.GetProperty("startupMode").GetString());
                Assert.Equal("test-group", pod.GetProperty("functionGroupName").GetString());
                Assert.False(pod.GetProperty("isAlwaysReady").GetBoolean());
                Assert.DoesNotContain("private-value", body);
                Assert.DoesNotContain("environment", body);
                Assert.DoesNotContain("runtimePodName", body);
            }

            long readyRevision = manager.State.Revision;
            await AssertReadinessAsync(management, "/admin/worker/ready", HttpStatusCode.OK, timeout.Token);
            string replayBody = assignmentBody.Replace(
                "\"B\":\"private-value\",\"A\":\"1\"", "\"A\":\"1\",\"B\":\"private-value\"", StringComparison.Ordinal);
            using (HttpResponseMessage replay = await PutManagementJsonAsync(
                management, "/admin/worker/assignment", replayBody, timeout.Token))
            {
                Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
                Assert.Empty(await replay.Content.ReadAsByteArrayAsync(timeout.Token));
            }

            string conflictingBody = assignmentBody.Replace("test-group", "other-group", StringComparison.Ordinal);
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
            StreamingMessage relayedInit = await worker.ReadAsync(timeout.Token);
            Assert.Equal(init, relayedInit);
            Assert.Equal("runtime-authoritative-directory", relayedInit.WorkerInitRequest.FunctionAppDirectory);
            StreamingMessage initialized = CreateInitResponse(init.RequestId, "http://localhost:1234/");
            StreamingMessage expected = initialized.Clone();
            expected.WorkerInitResponse.Capabilities["HttpUri"] = proxyEndpoint;
            expected.WorkerInitResponse.Capabilities["FunctionGroupName"] = "test-group";
            Task<WorkerStatePollResult> initializationPoll =
                manager.WaitForChangeAsync(manager.State.Revision, timeout.Token);
            await worker.WriteAsync(initialized, timeout.Token);
            Assert.Equal(expected, await runtime.ReadAsync(timeout.Token));
            Assert.True((await initializationPoll).State?.IsRuntimeInitialized);
            SplitWorkerOriginState running = await ReadSplitManagementStateAsync(management, timeout.Token);
            Assert.Equal(SplitWorkerStatus.Running, running.Status);
            Assert.Equal(SplitWorkerLinkState.Linked, running.Link.State);
            Assert.Equal("runtime-ns", running.Link.Runtime!.ResourceNamespace);
            Assert.Equal("runtime-pod", running.Link.Runtime.PodName);
            Assert.Equal("test-app", running.Link.Runtime.App.FunctionAppName);
            Assert.Equal(42, running.Link.Runtime.App.FunctionAppSiteId);

            Task<HttpResponseMessage> terminationPoll = management.GetAsync(
                "/admin/worker/state?lastKnownRevision=" +
                    manager.State.Revision.ToString(CultureInfo.InvariantCulture),
                timeout.Token);
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
                JsonElement pod = state.RootElement.GetProperty("workerPodState");
                Assert.Equal("None", pod.GetProperty("podStatus").GetString());
                Assert.Equal(startupMode, pod.GetProperty("startupMode").GetString());
                Assert.Equal("test-group", pod.GetProperty("functionGroupName").GetString());
                Assert.False(pod.GetProperty("isAlwaysReady").GetBoolean());
            }

            SplitWorkerOriginState terminated = await ReadSplitManagementStateAsync(management, timeout.Token);
            Assert.Equal(SplitWorkerStatus.Starting, terminated.Status);
            Assert.Equal(SplitWorkerLinkState.Unlinked, terminated.Link.State);
            Assert.Null(terminated.Link.Runtime);
            Assert.True(terminated.IsUnhealthy);
            await AssertReadinessAsync(management, "/admin/worker/ready", HttpStatusCode.ServiceUnavailable, timeout.Token);
            await AssertReadinessAsync(management, "/admin/instance/ready", HttpStatusCode.OK, timeout.Token);
            using HttpResponseMessage terminalReplay = await PutManagementJsonAsync(
                management, "/admin/worker/assignment", assignmentBody, timeout.Token);
            await AssertManagementErrorAsync(terminalReplay, HttpStatusCode.Conflict, "WorkerTerminated", timeout.Token);
            using HttpResponseMessage terminalConflict = await PutManagementJsonAsync(
                management, "/admin/worker/assignment", conflictingBody, timeout.Token);
            await AssertManagementErrorAsync(terminalConflict, HttpStatusCode.Conflict, "AssignmentConflict", timeout.Token);
            using JsonDocument current = await ReadManagementStateAsync(management, readyRevision, timeout.Token);
            Assert.Equal(manager.State.Revision, current.RootElement.GetProperty("revisionId").GetInt64());
        }
        finally
        {
            if (runtime is not null)
            {
                await runtime.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task ManagementApis_InvalidWorkerStartupMarksAssignmentUnhealthy()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);

        using HttpResponseMessage assignment = await PutManagementJsonAsync(
            management, "/admin/worker/assignment", ManagementAssignment, timeout.Token);
        Assert.Equal(HttpStatusCode.Created, assignment.StatusCode);

        Grpc.Core.RpcException exception = await worker.WriteAndReadRejectionAsync(CreateMessage("not-start-stream"), timeout.Token);

        Assert.Equal(StatusCode.Unavailable, exception.StatusCode);
        await AssertReadinessAsync(management, "/admin/worker/ready", HttpStatusCode.ServiceUnavailable, timeout.Token);
        Assert.Equal(WorkerAssignmentState.Failed, manager.State.AssignmentState);
        SplitWorkerOriginState failed = await ReadSplitManagementStateAsync(management, timeout.Token);
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

    [Fact]
    public async Task SplitInstanceState_InitializationBeforeAssignment_RemainsUnlinked()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        Metadata runtimeHeaders = new()
        {
            { "x-ms-functions-runtime-identity-bin", Encoding.UTF8.GetBytes(RuntimeIdentityJson) }
        };
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
        await runtime.ReadAsync(timeout.Token);
        Assert.False(manager.State.RuntimeInitialized);

        await AssignWorkerAsync(management, timeout.Token);

        Assert.Equal(WorkerAssignmentState.Ready, manager.State.AssignmentState);
        Assert.False(manager.State.IsRuntimeInitialized);
        SplitWorkerOriginState state = await ReadSplitManagementStateAsync(management, timeout.Token);
        Assert.Equal(SplitWorkerStatus.Starting, state.Status);
        Assert.Equal(SplitWorkerLinkState.Unlinked, state.Link.State);
    }

    [Fact]
    public async Task SplitInstanceState_Startup_PublishesFlattenedCheckpoint()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);

        using HttpResponseMessage placeholderResponse = await PollSplitManagementStateAsync(
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
            "isDiscoveryPod",
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

        SplitWorkerOriginState placeholder = Assert.IsType<SplitWorkerOriginState>(
            JsonSerializer.Deserialize(placeholderJson, WorkerProxyJsonContext.Default.SplitWorkerOriginState));
        Assert.Equal(SplitWorkerOriginState.ContainerType, placeholder.FunctionsContainerType);
        Assert.Equal("unknown", placeholder.Self.ResourceNamespace);
        Assert.Equal("test-worker-pod", placeholder.Self.PodName);
        Assert.Equal(new SplitFunctionAppIdentity("unknown", 0, "unknown"), placeholder.Self.App);
        Assert.False(placeholder.IsDiscoveryPod);
        Assert.False(placeholder.IsAlwaysReady);
        Assert.Null(placeholder.FunctionGroup);
        Assert.Equal(0, placeholder.ConfigLastModifiedTime);
        Assert.Equal(0, placeholder.ContentLastModifiedTime);
        Assert.NotEqual(default(DateTimeOffset), placeholder.StartupTime);
        Assert.Equal(0, placeholder.RevisionId);
        Assert.Equal(SplitWorkerStatus.Starting, placeholder.Status);
        Assert.Equal(new SplitWorkerLink(SplitWorkerLinkState.Unlinked), placeholder.Link);
        Assert.False(placeholder.IsUnhealthy);

        // Verify Legion NNA echo compatibility when null properties (e.g. functionGroup at Revision 0) are omitted.
        System.Text.Json.Nodes.JsonObject nnaEcho = System.Text.Json.Nodes.JsonNode.Parse(placeholderJson)!.AsObject();
        nnaEcho.Remove("functionGroup");
        nnaEcho["revisionId"] = -1;
        using StringContent echoContent = new(nnaEcho.ToJsonString(), Encoding.UTF8, "application/json");
        using HttpResponseMessage echoResponse = await management.PostAsync(InfraStatePath, echoContent, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, echoResponse.StatusCode);
    }

    [Fact]
    public async Task SplitInstanceState_Assignment_UpdatesLongPoll()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        SplitWorkerOriginState placeholder = await ReadSplitManagementStateAsync(management, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await worker.WriteAsync(CreateStartStream(), timeout.Token);
        await WaitForWorkerReadyAsync(manager, timeout.Token);

        SplitWorkerOriginState preAssignment = await ReadSplitManagementStateAsync(management, timeout.Token);
        Assert.Equal(2, preAssignment.RevisionId);
        Assert.Equal(SplitWorkerStatus.Starting, preAssignment.Status);
        Assert.Equal(new SplitWorkerLink(SplitWorkerLinkState.Unlinked), preAssignment.Link);

        Task<HttpResponseMessage> assignmentPoll = PollSplitManagementStateAsync(
            management,
            preAssignment,
            timeout.Token);
        await WaitForManagementPollAsync(manager, timeout.Token);
        Assert.False(assignmentPoll.IsCompleted);
        const string legacyStateFields =
            "\"isAlwaysReady\":false,\"configLastModifiedTime\":111,\"contentLastModifiedTime\":222";
        string legacyAssignment = ManagementAssignment
            .Replace("\"functionGroupName\":\"test-group\"", "\"functionGroupName\":\"deployment:validation\"", StringComparison.Ordinal)
            .Replace("\"isAlwaysReady\":false", legacyStateFields, StringComparison.Ordinal);
        using (HttpResponseMessage assignmentResponse = await PutManagementJsonAsync(
            management,
            "/admin/worker/assignment",
            legacyAssignment,
            timeout.Token))
        {
            Assert.Equal(HttpStatusCode.Created, assignmentResponse.StatusCode);
        }

        using HttpResponseMessage assignedResponse = await assignmentPoll;
        SplitWorkerOriginState assigned = await ReadSplitManagementStateAsync(assignedResponse, timeout.Token);
        Assert.Equal(placeholder.Self, assigned.Self);
        Assert.False(assigned.IsDiscoveryPod);
        Assert.False(assigned.IsAlwaysReady);
        Assert.Equal("deployment:validation", assigned.FunctionGroup);
        Assert.Equal(0, assigned.ConfigLastModifiedTime);
        Assert.Equal(0, assigned.ContentLastModifiedTime);
        Assert.Equal(placeholder.StartupTime, assigned.StartupTime);
        Assert.Equal(3, assigned.RevisionId);
        Assert.Equal(SplitWorkerStatus.Starting, assigned.Status);
        Assert.Equal(new SplitWorkerLink(SplitWorkerLinkState.Unlinked), assigned.Link);
        Assert.False(assigned.IsUnhealthy);

        using HttpResponseMessage differentStateResponse = await PollSplitManagementStateAsync(
            management,
            assigned with { FunctionsContainerType = "FunctionsSplitRuntime" },
            timeout.Token);
        Assert.Equal(assigned, await ReadSplitManagementStateAsync(differentStateResponse, timeout.Token));
    }

    [Fact]
    public async Task SplitInstanceState_RuntimeAttachAndWorkerTermination_KeepsDeferredLinkDefaults()
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
        SplitWorkerOriginState assigned = await ReadSplitManagementStateAsync(management, timeout.Token);

        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await runtime.WriteAsync(CreateMessage("runtime-attach"), timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);

        Assert.Equal(assigned.RevisionId, manager.State.Revision);
        Assert.Equal(assigned, await ReadSplitManagementStateAsync(management, timeout.Token));
        Assert.Equal(SplitWorkerStatus.Starting, assigned.Status);
        Assert.Equal(new SplitWorkerLink(SplitWorkerLinkState.Unlinked), assigned.Link);

        Task<HttpResponseMessage> terminationPoll = PollSplitManagementStateAsync(
            management,
            assigned,
            timeout.Token);
        await WaitForManagementPollAsync(manager, timeout.Token);
        Assert.False(terminationPoll.IsCompleted);
        await worker.CompleteRequestAsync(timeout.Token);
        await Task.WhenAll(
            runtime.WaitForTerminationAsync(timeout.Token),
            worker.WaitForTerminationAsync(timeout.Token));

        using HttpResponseMessage terminatedResponse = await terminationPoll;
        SplitWorkerOriginState terminated = await ReadSplitManagementStateAsync(terminatedResponse, timeout.Token);
        Assert.Equal(assigned.Self, terminated.Self);
        Assert.Equal(assigned.IsAlwaysReady, terminated.IsAlwaysReady);
        Assert.Equal(assigned.FunctionGroup, terminated.FunctionGroup);
        Assert.Equal(assigned.StartupTime, terminated.StartupTime);
        Assert.Equal(assigned.RevisionId + 1, terminated.RevisionId);
        Assert.Equal(SplitWorkerStatus.Starting, terminated.Status);
        Assert.Equal(new SplitWorkerLink(SplitWorkerLinkState.Unlinked), terminated.Link);
        Assert.True(terminated.IsUnhealthy);
    }

    private static async Task<HttpResponseMessage> PutManagementJsonAsync(
        HttpClient client, string path, string body, CancellationToken cancellationToken)
    {
        using StringContent content = new(body, Encoding.UTF8, "application/json");
        return await client.PutAsync(path, content, cancellationToken);
    }

    private static async Task<JsonDocument> ReadManagementStateAsync(
        HttpClient client, long? lastKnownRevision, CancellationToken cancellationToken)
    {
        string path = lastKnownRevision.HasValue
            ? "/admin/worker/state?lastKnownRevision=" + lastKnownRevision.Value.ToString(CultureInfo.InvariantCulture)
            : "/admin/worker/state";
        using HttpResponseMessage response = await client.GetAsync(path, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
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

    private static async Task<SplitWorkerOriginState> ReadSplitManagementStateAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await PollSplitManagementStateAsync(
            client,
            previousState: null,
            cancellationToken);
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

    private static async Task WaitForWorkerReadyAsync(
        WorkerPodStateManager manager,
        CancellationToken cancellationToken)
    {
        while (!manager.State.IsWorkerReady)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }

    private static async Task AssertReadinessAsync(
        HttpClient client, string path, HttpStatusCode statusCode, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync(path, cancellationToken);
        Assert.Equal(statusCode, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(cancellationToken));
        if (string.Equals(path, "/admin/worker/ready", StringComparison.Ordinal))
        {
            Assert.True(response.Headers.CacheControl?.NoStore);
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
