// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Control;
using Azure.Functions.WorkerProxy.Management;
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ControlSplitFunctionAppIdentity = Azure.Functions.WorkerProxy.Control.SplitFunctionAppIdentity;
using ControlSplitPodIdentity = Azure.Functions.WorkerProxy.Control.SplitPodIdentity;
using GrpcRpcException = Grpc.Core.RpcException;
using StateSplitPodIdentity = Azure.Functions.WorkerProxy.State.SplitPodIdentity;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    private const string InfraStatePath = "/admin/infra/instanceState";
    private const string ManagementAssignment = """
        {
          "startupMode":"SpecializationRequired",
          "functionAppName":"test-app",
          "functionGroupName":"test-group",
          "isAlwaysReady":false,
          "environment":{"B":"private-value","A":"1"},
          "functionAppDirectory":"/home/site/wwwroot",
          "httpProxyEndpoint":"100.86.0.4:28080"
        }
        """;

    [Theory]
    [InlineData(true, "Preconfigured")]
    [InlineData(false, "Preconfigured")]
    [InlineData(true, "SpecializationRequired")]
    [InlineData(false, "SpecializationRequired")]
    public async Task ManagementApis_ObserveNormalStartupAssignmentAndTerminalFailure(bool runtimeFirst, string startupMode)
    {
        string assignmentBody = ManagementAssignment.Replace("SpecializationRequired", startupMode, StringComparison.Ordinal);
        const string proxyEndpoint = "http://100.86.0.4:28080/";
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        await AssertReadinessAsync(management, "/admin/instance/ready", HttpStatusCode.OK, timeout.Token);
        await AssertReadinessAsync(management, "/admin/worker/ready", HttpStatusCode.ServiceUnavailable, timeout.Token);

        Assert.Equal(0, manager.State.Revision);
        StreamingMessage init = new()
        {
            RequestId = "runtime-init",
            WorkerInitRequest = new() { HostVersion = "test-host", FunctionAppDirectory = "runtime-authoritative-directory" }
        };
        RelayClient? runtime = null;
        try
        {
            if (runtimeFirst)
            {
                runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
                await runtime.WriteAsync(init, timeout.Token);
                await WaitForAttachmentAsync(factory.Services.GetRequiredService<FunctionRpcRelay>(),
                    FunctionRpcRelaySide.Runtime, timeout.Token);
                await AssertReadinessAsync(management, "/admin/worker/ready", HttpStatusCode.ServiceUnavailable, timeout.Token);
            }

            await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
            StreamingMessage start = CreateStartStream();
            await worker.WriteAsync(start, timeout.Token);
            while (!manager.State.IsWorkerReady)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
            }

            await AssertReadinessAsync(management, "/admin/worker/ready", HttpStatusCode.OK, timeout.Token);
            long unassignedRevision = runtimeFirst ? 3 : 2;
            using (JsonDocument unassigned = await ReadManagementStateAsync(management, null, timeout.Token))
            {
                Assert.Equal(unassignedRevision, unassigned.RootElement.GetProperty("revisionId").GetInt64());
                Assert.Equal("None", unassigned.RootElement.GetProperty("workerPodState").GetProperty("podStatus").GetString());
                Assert.False(unassigned.RootElement.GetProperty("workerPodState").TryGetProperty("startupMode", out _));
            }

            Task<HttpResponseMessage> assignmentPoll = management.GetAsync(
                $"/admin/worker/state?lastKnownRevision={unassignedRevision.ToString(CultureInfo.InvariantCulture)}",
                timeout.Token);
            await WaitForManagementPollAsync(manager, timeout.Token);
            using (HttpResponseMessage assignment = await PutManagementJsonAsync(
                management, "/admin/worker/assignment", assignmentBody, timeout.Token))
            {
                Assert.Equal(HttpStatusCode.Created, assignment.StatusCode);
                Assert.Equal("/admin/worker/assignment", assignment.Headers.Location?.OriginalString);
                Assert.Empty(await assignment.Content.ReadAsByteArrayAsync(timeout.Token));
            }

            using (HttpResponseMessage changed = await assignmentPoll)
            {
                Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
                Assert.True(changed.Headers.CacheControl?.NoStore);
                string body = await changed.Content.ReadAsStringAsync(timeout.Token);
                using JsonDocument state = JsonDocument.Parse(body);
                Assert.Equal("FunctionsWorkerPod", state.RootElement.GetProperty("functionsContainerType").GetString());
                Assert.Equal("test-worker-pod", state.RootElement.GetProperty("podName").GetString());
                Assert.Equal(unassignedRevision + 1, state.RootElement.GetProperty("revisionId").GetInt64());
                JsonElement pod = state.RootElement.GetProperty("workerPodState");
                Assert.Equal("ReadyForRequest", pod.GetProperty("podStatus").GetString());
                Assert.Equal(startupMode, pod.GetProperty("startupMode").GetString());
                Assert.Equal("test-group", pod.GetProperty("functionGroupName").GetString());
                Assert.False(pod.GetProperty("isAlwaysReady").GetBoolean());
                Assert.Equal(new Uri(proxyEndpoint), manager.State.HttpProxyEndpoint);
                Assert.DoesNotContain("private-value", body);
                Assert.DoesNotContain("environment", body);
                Assert.DoesNotContain("runtimePodName", body);
            }

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

            Assert.Equal(unassignedRevision + 1, manager.State.Revision);
            if (runtime is null)
            {
                runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
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
            await worker.WriteAsync(initialized, timeout.Token);
            Assert.Equal(expected, await runtime.ReadAsync(timeout.Token));
            const long activeRevision = 4;
            while (manager.State.Revision < activeRevision)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
            }

            Assert.Equal(activeRevision, manager.State.Revision);

            Task<HttpResponseMessage> terminationPoll = management.GetAsync(
                $"/admin/worker/state?lastKnownRevision={activeRevision.ToString(CultureInfo.InvariantCulture)}",
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
                Assert.Equal(activeRevision + 1, state.RootElement.GetProperty("revisionId").GetInt64());
                JsonElement pod = state.RootElement.GetProperty("workerPodState");
                Assert.Equal("None", pod.GetProperty("podStatus").GetString());
                Assert.Equal(startupMode, pod.GetProperty("startupMode").GetString());
                Assert.Equal("test-group", pod.GetProperty("functionGroupName").GetString());
                Assert.False(pod.GetProperty("isAlwaysReady").GetBoolean());
            }

            await AssertReadinessAsync(management, "/admin/worker/ready", HttpStatusCode.ServiceUnavailable, timeout.Token);
            await AssertReadinessAsync(management, "/admin/instance/ready", HttpStatusCode.OK, timeout.Token);
            using HttpResponseMessage terminalReplay = await PutManagementJsonAsync(
                management, "/admin/worker/assignment", assignmentBody, timeout.Token);
            await AssertManagementErrorAsync(terminalReplay, HttpStatusCode.Conflict, "WorkerTerminated", timeout.Token);
            using HttpResponseMessage terminalConflict = await PutManagementJsonAsync(
                management, "/admin/worker/assignment", conflictingBody, timeout.Token);
            await AssertManagementErrorAsync(terminalConflict, HttpStatusCode.Conflict, "AssignmentConflict", timeout.Token);
            using JsonDocument current = await ReadManagementStateAsync(management, 2, timeout.Token);
            Assert.Equal(activeRevision + 1, current.RootElement.GetProperty("revisionId").GetInt64());
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
    public async Task ManagementApis_InvalidWorkerStartupNeverBecomesReady()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);

        Grpc.Core.RpcException exception = await worker.WriteAndReadRejectionAsync(CreateMessage("not-start-stream"), timeout.Token);

        Assert.Equal(StatusCode.Unavailable, exception.StatusCode);
        await AssertReadinessAsync(management, "/admin/worker/ready", HttpStatusCode.ServiceUnavailable, timeout.Token);
        using HttpResponseMessage assignment = await PutManagementJsonAsync(
            management, "/admin/worker/assignment", ManagementAssignment, timeout.Token);
        Assert.Equal(HttpStatusCode.Created, assignment.StatusCode);
        Assert.Equal(WorkerPodStatus.None, factory.Services.GetRequiredService<WorkerPodStateManager>().State.PodStatus);
    }

    [Fact]
    public async Task SplitInstanceStatePublishesPlaceholderAndAssignmentState()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);

        using HttpResponseMessage placeholderResponse = await PollSplitManagementStateAsync(
            management,
            previousState: null,
            timeout.Token);
        Assert.Equal(HttpStatusCode.OK, placeholderResponse.StatusCode);
        string placeholderJson = await placeholderResponse.Content.ReadAsStringAsync(timeout.Token);
        using JsonDocument wireState = JsonDocument.Parse(placeholderJson);
        JsonElement wireRoot = wireState.RootElement;
        Assert.False(wireRoot.TryGetProperty("staticState", out _));
        Assert.False(wireRoot.TryGetProperty("dynamicState", out _));
        Assert.Equal("Starting", wireRoot.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, wireRoot.GetProperty("functionGroup").ValueKind);
        JsonElement wireLink = wireRoot.GetProperty("link");
        Assert.Equal("Unlinked", wireLink.GetProperty("state").GetString());
        Assert.False(wireLink.TryGetProperty("runtime", out _));

        SplitWorkerOriginState? deserializedPlaceholder = JsonSerializer.Deserialize(
            placeholderJson,
            WorkerProxyJsonContext.Default.SplitWorkerOriginState);
        SplitWorkerOriginState placeholder = Assert.IsType<SplitWorkerOriginState>(deserializedPlaceholder);
        Assert.Equal("FunctionsSplitWorkerProxy", placeholder.FunctionsContainerType);
        Assert.Equal("test-worker-pod", placeholder.Self.PodName);
        Assert.Equal(Guid.Empty, placeholder.Self.PodGuid);
        Assert.False(placeholder.IsDiscoveryPod);
        Assert.False(placeholder.IsAlwaysReady);
        Assert.Null(placeholder.FunctionGroup);
        Assert.Equal(0, placeholder.ConfigLastModifiedTime);
        Assert.Equal(0, placeholder.ContentLastModifiedTime);
        Assert.Equal(0, placeholder.RevisionId);
        Assert.Equal(SplitWorkerStatus.Starting, placeholder.Status);
        Assert.Equal(new SplitWorkerLink(SplitWorkerLinkState.Unlinked), placeholder.Link);
        Assert.False(placeholder.IsUnhealthy);

        JsonObject malformedPreviousState = JsonNode.Parse(placeholderJson)!.AsObject();
        Assert.True(malformedPreviousState["link"]!.AsObject().Remove("state"));
        using (StringContent malformedContent = new(
            malformedPreviousState.ToJsonString(),
            Encoding.UTF8,
            "application/json"))
        using (HttpResponseMessage malformedResponse = await management.PostAsync(
            InfraStatePath,
            malformedContent,
            timeout.Token))
        {
            Assert.Equal(HttpStatusCode.BadRequest, malformedResponse.StatusCode);
        }

        using (HttpResponseMessage assignmentResponse = await PutManagementJsonAsync(
            management,
            "/admin/worker/assignment",
            ManagementAssignment,
            timeout.Token))
        {
            Assert.Equal(HttpStatusCode.Created, assignmentResponse.StatusCode);
        }

        SplitWorkerOriginState assigned = await ReadSplitManagementStateAsync(management, timeout.Token);
        Assert.Equal(placeholder.Self, assigned.Self);
        Assert.False(assigned.IsAlwaysReady);
        Assert.Equal("test-group", assigned.FunctionGroup);
        Assert.Equal(placeholder.StartupTime, assigned.StartupTime);
        Assert.Equal(1, assigned.RevisionId);
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
    public async Task SplitRuntimeRelayPublishesLinkingThenAcknowledgedRuntimeUntilTermination()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await worker.WriteAsync(CreateStartStream(), timeout.Token);
        await WaitForWorkerReadyAsync(management, timeout.Token);
        await AssignWorkerAsync(management, timeout.Token);

        SplitWorkerOriginState unlinked = await ReadSplitManagementStateAsync(management, timeout.Token);
        Task<HttpResponseMessage> linkingPoll = PollSplitManagementStateAsync(
            management,
            unlinked,
            timeout.Token);
        await WaitForManagementPollAsync(manager, timeout.Token);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        Assert.Equal("test-worker", (await runtime.ReadAsync(timeout.Token)).StartStream.WorkerId);

        using HttpResponseMessage linkingResponse = await linkingPoll;
        SplitWorkerOriginState linking = await ReadSplitManagementStateAsync(linkingResponse, timeout.Token);
        Assert.Equal(4, linking.RevisionId);
        Assert.Equal(SplitWorkerStatus.Starting, linking.Status);
        Assert.Equal(new SplitWorkerLink(SplitWorkerLinkState.Linking), linking.Link);

        Task<HttpResponseMessage> linkedPoll = PollSplitManagementStateAsync(
            management,
            linking,
            timeout.Token);
        await WaitForManagementPollAsync(manager, timeout.Token);
        using GrpcChannel controlChannel = GrpcChannel.ForAddress(factory.GetFunctionRpcAddress(FunctionRpcRelaySide.Runtime));
        WorkerLinkControl.WorkerLinkControlClient controlClient = new(controlChannel);
        AcknowledgeLinkRequest acknowledgement = CreateRuntimeAcknowledgement();
        await controlClient.AcknowledgeLinkAsync(acknowledgement, cancellationToken: timeout.Token);

        using HttpResponseMessage linkedResponse = await linkedPoll;
        SplitWorkerOriginState linked = await ReadSplitManagementStateAsync(linkedResponse, timeout.Token);
        Assert.Equal(5, linked.RevisionId);
        Assert.Equal(SplitWorkerStatus.Running, linked.Status);
        Assert.Equal(SplitWorkerLinkState.Linked, linked.Link.State);
        AssertRuntimeIdentity(linked.Link.Runtime);

        await controlClient.AcknowledgeLinkAsync(acknowledgement, cancellationToken: timeout.Token);
        Assert.Equal(linked, await ReadSplitManagementStateAsync(management, timeout.Token));

        AcknowledgeLinkRequest conflict = CreateRuntimeAcknowledgement();
        conflict.Runtime.PodGuid = "22222222-2222-2222-2222-222222222222";
        GrpcRpcException conflictException = await Assert.ThrowsAsync<GrpcRpcException>(
            async () =>
            {
                await controlClient.AcknowledgeLinkAsync(conflict, cancellationToken: timeout.Token);
            });
        Assert.Equal(StatusCode.AlreadyExists, conflictException.StatusCode);
        Assert.Equal(linked, await ReadSplitManagementStateAsync(management, timeout.Token));

        Task<HttpResponseMessage> terminatedPoll = PollSplitManagementStateAsync(management, linked, timeout.Token);
        await WaitForManagementPollAsync(manager, timeout.Token);

        await runtime.CompleteRequestAsync(timeout.Token);
        Assert.Equal(StatusCode.Unavailable, await runtime.WaitForTerminationAsync(timeout.Token));
        Assert.Equal(StatusCode.Unavailable, await worker.WaitForTerminationAsync(timeout.Token));

        using HttpResponseMessage terminatedResponse = await terminatedPoll;
        SplitWorkerOriginState terminated = await ReadSplitManagementStateAsync(terminatedResponse, timeout.Token);
        Assert.Equal(6, terminated.RevisionId);
        Assert.Equal(SplitWorkerStatus.Starting, terminated.Status);
        Assert.Equal(SplitWorkerLinkState.Linked, terminated.Link.State);
        AssertRuntimeIdentity(terminated.Link.Runtime);
        Assert.True(terminated.IsUnhealthy);
    }

    [Fact]
    public async Task SplitRuntimeLinkRejectsSecondConnectionAndPreservesLegacyState()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await worker.WriteAsync(CreateStartStream(), timeout.Token);
        await WaitForWorkerReadyAsync(management, timeout.Token);
        await AssignWorkerAsync(management, timeout.Token);

        SplitWorkerOriginState unlinked = await ReadSplitManagementStateAsync(management, timeout.Token);
        Task<HttpResponseMessage> linkingPoll = PollSplitManagementStateAsync(management, unlinked, timeout.Token);
        await WaitForManagementPollAsync(manager, timeout.Token);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        Assert.Equal("test-worker", (await runtime.ReadAsync(timeout.Token)).StartStream.WorkerId);
        using HttpResponseMessage linkingResponse = await linkingPoll;
        SplitWorkerOriginState linking = await ReadSplitManagementStateAsync(linkingResponse, timeout.Token);
        Assert.Equal(SplitWorkerLinkState.Linking, linking.Link.State);
        Assert.Null(linking.Link.Runtime);

        await using RelayClient conflictingRuntime =
            CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        GrpcRpcException exception =
            await conflictingRuntime.WriteAndReadRejectionAsync(CreateMessage("conflict"), timeout.Token);
        Assert.Equal(StatusCode.AlreadyExists, exception.StatusCode);

        Assert.Equal(linking, await ReadSplitManagementStateAsync(management, timeout.Token));

        using JsonDocument legacy = await ReadManagementStateAsync(management, lastKnownRevision: null, timeout.Token);
        Assert.Equal("FunctionsWorkerPod", legacy.RootElement.GetProperty("functionsContainerType").GetString());
        Assert.Equal(4, legacy.RootElement.GetProperty("revisionId").GetInt64());
        Assert.True(legacy.RootElement.TryGetProperty("workerPodState", out _));
        Assert.False(legacy.RootElement.TryGetProperty("staticState", out _));
        Assert.False(legacy.RootElement.TryGetProperty("dynamicState", out _));
    }

    [Theory]
    [InlineData("runtime")]
    [InlineData("resourceNamespace")]
    [InlineData("podName")]
    [InlineData("podGuid")]
    [InlineData("legionStampName")]
    [InlineData("podType")]
    [InlineData("app")]
    [InlineData("functionAppName")]
    [InlineData("functionAppSiteId")]
    [InlineData("antaresStampName")]
    public async Task WorkerLinkControlRejectsIncompleteRuntimeIdentity(string field)
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        WorkerPodState before = manager.State;
        using CancellationTokenSource timeout = new(TestTimeout);
        using GrpcChannel channel = GrpcChannel.ForAddress(factory.GetFunctionRpcAddress(FunctionRpcRelaySide.Runtime));
        WorkerLinkControl.WorkerLinkControlClient client = new(channel);
        AcknowledgeLinkRequest request = CreateRuntimeAcknowledgement();
        switch (field)
        {
            case "runtime":
                request.Runtime = null;
                break;
            case "resourceNamespace":
                request.Runtime.ResourceNamespace = string.Empty;
                break;
            case "podName":
                request.Runtime.PodName = string.Empty;
                break;
            case "podGuid":
                request.Runtime.PodGuid = string.Empty;
                break;
            case "legionStampName":
                request.Runtime.LegionStampName = string.Empty;
                break;
            case "podType":
                request.Runtime.PodType = "FunctionsSplitWorkerProxy";
                break;
            case "app":
                request.Runtime.App = null;
                break;
            case "functionAppName":
                request.Runtime.App.FunctionAppName = string.Empty;
                break;
            case "functionAppSiteId":
                request.Runtime.App.ClearFunctionAppSiteId();
                break;
            case "antaresStampName":
                request.Runtime.App.AntaresStampName = string.Empty;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field));
        }

        GrpcRpcException exception = await Assert.ThrowsAsync<GrpcRpcException>(
            async () =>
            {
                await client.AcknowledgeLinkAsync(request, cancellationToken: timeout.Token);
            });

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
        Assert.Same(before, manager.State);
    }

    [Fact]
    public async Task WorkerLinkControlRejectsWorkerListener()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        using CancellationTokenSource timeout = new(TestTimeout);
        using GrpcChannel channel = GrpcChannel.ForAddress(factory.GetFunctionRpcAddress(FunctionRpcRelaySide.Worker));
        WorkerLinkControl.WorkerLinkControlClient client = new(channel);

        GrpcRpcException exception = await Assert.ThrowsAsync<GrpcRpcException>(
            async () =>
            {
                await client.AcknowledgeLinkAsync(
                    CreateRuntimeAcknowledgement(),
                    cancellationToken: timeout.Token);
            });

        Assert.Equal(StatusCode.Unimplemented, exception.StatusCode);
    }

    private static AcknowledgeLinkRequest CreateRuntimeAcknowledgement()
    {
        return new()
        {
            Runtime = new ControlSplitPodIdentity
            {
                ResourceNamespace = "test-runtime-namespace",
                PodName = "test-runtime-pod",
                PodGuid = "11111111-1111-1111-1111-111111111111",
                LegionStampName = "test-legion-stamp",
                PodType = "FunctionsSplitRuntimePod",
                App = new ControlSplitFunctionAppIdentity
                {
                    FunctionAppName = "test-app",
                    FunctionAppSiteId = 42,
                    AntaresStampName = "test-antares-stamp"
                }
            }
        };
    }

    private static void AssertRuntimeIdentity(StateSplitPodIdentity? runtime)
    {
        StateSplitPodIdentity identity = Assert.IsType<StateSplitPodIdentity>(runtime);
        Assert.Equal("test-runtime-namespace", identity.ResourceNamespace);
        Assert.Equal("test-runtime-pod", identity.PodName);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), identity.PodGuid);
        Assert.Equal("test-legion-stamp", identity.LegionStampName);
        Assert.Equal("FunctionsSplitRuntimePod", identity.PodType);
        Assert.Equal("test-app", identity.App.FunctionAppName);
        Assert.Equal(42, identity.App.FunctionAppSiteId);
        Assert.Equal("test-antares-stamp", identity.App.AntaresStampName);
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

    private static async Task WaitForWorkerReadyAsync(HttpClient client, CancellationToken cancellationToken)
    {
        while (true)
        {
            using HttpResponseMessage response = await client.GetAsync("/admin/worker/ready", cancellationToken);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return;
            }

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
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
}
