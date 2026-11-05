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

    [Theory]
    [InlineData(true, "Preconfigured")]
    [InlineData(false, "Preconfigured")]
    [InlineData(true, "SpecializationRequired")]
    [InlineData(false, "SpecializationRequired")]
    public async Task ManagementApis_AcceptAssignmentBeforeEitherStreamAndPreserveItThroughInitializationAndFailure(bool runtimeFirst, string startupMode)
    {
        string assignmentBody = ManagementAssignment.Replace("SpecializationRequired", startupMode, StringComparison.Ordinal);
        const string proxyEndpoint = "https://worker-pod:28080/";
        await using WorkerProxyWebApplicationFactory factory = new();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        using (HttpResponseMessage initial = await PostManagementStateAsync(management, null, timeout.Token))
        {
            Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        }

        using (HttpResponseMessage assignment = await PutManagementJsonAsync(
            management, "/admin/worker/assignment", assignmentBody, timeout.Token))
        {
            Assert.Equal(HttpStatusCode.Created, assignment.StatusCode);
            Assert.Equal("/admin/worker/assignment", assignment.Headers.Location?.OriginalString);
        }
        Assert.False(manager.State.IsWorkerReady);
        Assert.Null(manager.State.SessionId);
        Assert.Equal(1, manager.State.Revision);

        string replayBody = assignmentBody.Replace(
            "\"B\":\"private-value\",\"A\":\"1\"", "\"A\":\"1\",\"B\":\"private-value\"", StringComparison.Ordinal);
        using (HttpResponseMessage replay = await PutManagementJsonAsync(
            management, "/admin/worker/assignment", replayBody, timeout.Token))
        {
            Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        }
        string conflictingBody = assignmentBody.Replace("test-group", "other-group", StringComparison.Ordinal);
        using (HttpResponseMessage conflict = await PutManagementJsonAsync(
            management, "/admin/worker/assignment", conflictingBody, timeout.Token))
        {
            await AssertManagementErrorAsync(conflict, HttpStatusCode.Conflict, "AssignmentConflict", timeout.Token);
        }

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
                Assert.False(manager.State.IsWorkerReady);
            }

            await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
            StreamingMessage start = CreateStartStream();
            await worker.WriteAsync(start, timeout.Token);
            while (!manager.State.IsWorkerReady)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
            }
            Assert.Equal(3, manager.State.Revision);
            Assert.Equal("test-app", manager.State.FunctionAppName);
            Assert.Equal(startupMode, manager.State.StartupMode.ToString());
            using (JsonDocument state = await ReadManagementStateAsync(management, null, timeout.Token))
            {
                AssertStartingState(state.RootElement, revision: 3, unhealthy: false);
            }

            runtime ??= CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
            if (!runtimeFirst)
            {
                await runtime.WriteAsync(init, timeout.Token);
            }
            Assert.Equal(start, await runtime.ReadAsync(timeout.Token));
            Assert.Equal(init, await worker.ReadAsync(timeout.Token));
            StreamingMessage initialized = CreateInitResponse(init.RequestId, "http://localhost:1234/");
            StreamingMessage expected = initialized.Clone();
            expected.WorkerInitResponse.Capabilities["HttpUri"] = proxyEndpoint;
            expected.WorkerInitResponse.Capabilities["FunctionGroupName"] = "test-group";
            await worker.WriteAsync(initialized, timeout.Token);
            Assert.Equal(expected, await runtime.ReadAsync(timeout.Token));
            string lastState;
            using (JsonDocument state = await ReadManagementStateAsync(management, null, timeout.Token))
            {
                AssertStartingState(state.RootElement, revision: 3, unhealthy: false);
                lastState = state.RootElement.GetRawText();
            }

            using StringContent lastStateBody = new(lastState, Encoding.UTF8, "application/json");
            Task<HttpResponseMessage> terminationPoll = management.PostAsync("/admin/infra/instanceState", lastStateBody, timeout.Token);
            await WaitForManagementPollAsync(manager, timeout.Token);
            await worker.CompleteRequestAsync(timeout.Token);
            Assert.Equal(StatusCode.Unavailable, await runtime.WaitForTerminationAsync(timeout.Token));
            Assert.Equal(StatusCode.Unavailable, await worker.WaitForTerminationAsync(timeout.Token));
            using (HttpResponseMessage changed = await terminationPoll)
            {
                Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
                Assert.True(changed.Headers.CacheControl?.NoStore);
                using JsonDocument state = JsonDocument.Parse(await changed.Content.ReadAsStringAsync(timeout.Token));
                AssertStartingState(state.RootElement, revision: 4, unhealthy: true);
            }
            using HttpResponseMessage terminalReplay = await PutManagementJsonAsync(
                management, "/admin/worker/assignment", assignmentBody, timeout.Token);
            await AssertManagementErrorAsync(terminalReplay, HttpStatusCode.Conflict, "WorkerTerminated", timeout.Token);
            using HttpResponseMessage terminalConflict = await PutManagementJsonAsync(
                management, "/admin/worker/assignment", conflictingBody, timeout.Token);
            await AssertManagementErrorAsync(terminalConflict, HttpStatusCode.Conflict, "AssignmentConflict", timeout.Token);
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
    public async Task ManagementApis_InvalidWorkerStartupPreservesAcceptedAssignmentAsTerminalFailure()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpResponseMessage assignment = await PutManagementJsonAsync(
            management, "/admin/worker/assignment", ManagementAssignment, timeout.Token);
        Assert.Equal(HttpStatusCode.Created, assignment.StatusCode);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);

        Grpc.Core.RpcException exception = await worker.WriteAndReadRejectionAsync(CreateMessage("not-start-stream"), timeout.Token);

        Assert.Equal(StatusCode.Unavailable, exception.StatusCode);
        using HttpResponseMessage replay = await PutManagementJsonAsync(
            management, "/admin/worker/assignment", ManagementAssignment, timeout.Token);
        await AssertManagementErrorAsync(replay, HttpStatusCode.Conflict, "WorkerTerminated", timeout.Token);
        using JsonDocument state = await ReadManagementStateAsync(management, null, timeout.Token);
        AssertStartingState(state.RootElement, revision: 3, unhealthy: true);
    }

    private static void AssertStartingState(JsonElement state, int revision, bool unhealthy)
    {
        Assert.Equal("FunctionsSplitWorkerProxy", state.GetProperty("functionsContainerType").GetString());
        Assert.Equal(revision, state.GetProperty("revisionId").GetInt32());
        Assert.Equal("Starting", state.GetProperty("status").GetString());
        Assert.Equal("Unlinked", state.GetProperty("link").GetProperty("state").GetString());
        Assert.False(state.GetProperty("link").TryGetProperty("runtime", out _));
        Assert.Equal("test-group", state.GetProperty("functionGroup").GetString());
        Assert.Equal(unhealthy, state.GetProperty("isUnhealthy").GetBoolean());
        string json = state.GetRawText();
        Assert.DoesNotContain("private-value", json, StringComparison.Ordinal);
        Assert.DoesNotContain("environment", json, StringComparison.Ordinal);
        Assert.Equal("unknown", state.GetProperty("self").GetProperty("app").GetProperty("functionAppName").GetString());
        Assert.Equal(0, state.GetProperty("configLastModifiedTime").GetInt64());
        Assert.Equal(0, state.GetProperty("contentLastModifiedTime").GetInt64());
    }

    [Fact]
    public async Task InfraInstanceState_Startup_PublishesFlattenedCheckpoint()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);

        using HttpResponseMessage placeholderResponse = await PollInfraManagementStateAsync(
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
        Assert.Equal("FunctionsSplitWorkerProxy", wireRoot.GetProperty("functionsContainerType").GetString());
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
        Assert.Equal(WorkerOriginState.ContainerType, placeholder.FunctionsContainerType);
        Assert.Equal("unknown", placeholder.Self.ResourceNamespace);
        Assert.Equal("test-worker-pod", placeholder.Self.PodName);
        Assert.Equal(new FunctionAppIdentity("unknown", 0, "unknown"), placeholder.Self.App);
        Assert.False(placeholder.IsDiscoveryPod);
        Assert.False(placeholder.IsAlwaysReady);
        Assert.Null(placeholder.FunctionGroup);
        Assert.Equal(0, placeholder.ConfigLastModifiedTime);
        Assert.Equal(0, placeholder.ContentLastModifiedTime);
        Assert.NotEqual(default(DateTimeOffset), placeholder.StartupTime);
        Assert.Equal(0, placeholder.RevisionId);
        Assert.Equal(WorkerStatus.Starting, placeholder.Status);
        Assert.Equal(new WorkerLink(WorkerLinkState.Unlinked), placeholder.Link);
        Assert.False(placeholder.IsUnhealthy);
    }

    [Fact]
    public async Task InfraInstanceState_Assignment_UpdatesLongPoll()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        WorkerOriginState placeholder = await ReadInfraManagementStateAsync(management, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await worker.WriteAsync(CreateStartStream(), timeout.Token);
        await WaitForWorkerReadyAsync(manager, timeout.Token);

        WorkerOriginState preAssignment = await ReadInfraManagementStateAsync(management, timeout.Token);
        Assert.Equal(2, preAssignment.RevisionId);
        Assert.Equal(WorkerStatus.Starting, preAssignment.Status);
        Assert.Equal(new WorkerLink(WorkerLinkState.Unlinked), preAssignment.Link);

        Task<HttpResponseMessage> assignmentPoll = PollInfraManagementStateAsync(
            management,
            preAssignment,
            timeout.Token);
        await WaitForManagementPollAsync(manager, timeout.Token);
        Assert.False(assignmentPoll.IsCompleted);
        using (HttpResponseMessage assignmentResponse = await PutManagementJsonAsync(
            management,
            "/admin/worker/assignment",
            ManagementAssignment,
            timeout.Token))
        {
            Assert.Equal(HttpStatusCode.Created, assignmentResponse.StatusCode);
        }

        using HttpResponseMessage assignedResponse = await assignmentPoll;
        WorkerOriginState assigned = await ReadInfraManagementStateAsync(assignedResponse, timeout.Token);
        Assert.Equal(placeholder.Self, assigned.Self);
        Assert.False(assigned.IsAlwaysReady);
        Assert.Equal("test-group", assigned.FunctionGroup);
        Assert.Equal(placeholder.StartupTime, assigned.StartupTime);
        Assert.Equal(3, assigned.RevisionId);
        Assert.Equal(WorkerStatus.Starting, assigned.Status);
        Assert.Equal(new WorkerLink(WorkerLinkState.Unlinked), assigned.Link);
        Assert.False(assigned.IsUnhealthy);

        using HttpResponseMessage differentStateResponse = await PollInfraManagementStateAsync(
            management,
            assigned with { FunctionGroup = "stale-cached-group" },
            timeout.Token);
        Assert.Equal(assigned, await ReadInfraManagementStateAsync(differentStateResponse, timeout.Token));
    }

    [Fact]
    public async Task InfraInstanceState_RuntimeAttachAndWorkerTermination_KeepsDeferredLinkDefaults()
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
        WorkerOriginState assigned = await ReadInfraManagementStateAsync(management, timeout.Token);

        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await runtime.WriteAsync(CreateMessage("runtime-attach"), timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);

        Assert.Equal(assigned.RevisionId, manager.State.Revision);
        Assert.Equal(assigned, await ReadInfraManagementStateAsync(management, timeout.Token));
        Assert.Equal(WorkerStatus.Starting, assigned.Status);
        Assert.Equal(new WorkerLink(WorkerLinkState.Unlinked), assigned.Link);

        Task<HttpResponseMessage> terminationPoll = PollInfraManagementStateAsync(
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
        WorkerOriginState terminated = await ReadInfraManagementStateAsync(terminatedResponse, timeout.Token);
        Assert.Equal(assigned.Self, terminated.Self);
        Assert.Equal(assigned.IsAlwaysReady, terminated.IsAlwaysReady);
        Assert.Equal(assigned.FunctionGroup, terminated.FunctionGroup);
        Assert.Equal(assigned.StartupTime, terminated.StartupTime);
        Assert.Equal(assigned.RevisionId + 1, terminated.RevisionId);
        Assert.Equal(WorkerStatus.Starting, terminated.Status);
        Assert.Equal(new WorkerLink(WorkerLinkState.Unlinked), terminated.Link);
        Assert.True(terminated.IsUnhealthy);
    }

    private static async Task<HttpResponseMessage> PutManagementJsonAsync(
        HttpClient client, string path, string body, CancellationToken cancellationToken)
    {
        using StringContent content = new(body, Encoding.UTF8, "application/json");

        return await client.PutAsync(path, content, cancellationToken);
    }

    private static async Task<HttpResponseMessage> PostManagementStateAsync(
        HttpClient client, long? revision, CancellationToken cancellationToken)
    {
        string body = revision is { } value
            ? JsonSerializer.Serialize(
                (await ReadInfraManagementStateAsync(client, cancellationToken)) with { RevisionId = checked((int)value) },
                WorkerProxyJsonContext.Default.WorkerOriginState)
            : "null";
        using StringContent content = new(body, Encoding.UTF8, "application/json");

        return await client.PostAsync("/admin/infra/instanceState", content, cancellationToken);
    }

    private static async Task<JsonDocument> ReadManagementStateAsync(
        HttpClient client, long? lastKnownRevision, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await PostManagementStateAsync(client, lastKnownRevision, cancellationToken);
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

    private static async Task<WorkerOriginState> ReadInfraManagementStateAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await PollInfraManagementStateAsync(
            client,
            previousState: null,
            cancellationToken);
        return await ReadInfraManagementStateAsync(response, cancellationToken);
    }

    private static async Task<WorkerOriginState> ReadInfraManagementStateAsync(
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

    private static async Task<HttpResponseMessage> PollInfraManagementStateAsync(
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
