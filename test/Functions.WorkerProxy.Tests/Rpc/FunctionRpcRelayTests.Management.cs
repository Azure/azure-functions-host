// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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

    private const string SplitManagementAssignment = """
        {
          "startupMode":"SpecializationRequired",
          "functionAppName":"test-app",
          "functionGroupName":"test-group",
          "isAlwaysReady":false,
          "environment":{"SETTING":"private-value"},
          "functionAppDirectory":"/home/site/wwwroot",
          "staticState":{
            "self":{
              "resourceNamespace":"test-namespace",
              "podName":"test-worker-pod",
              "podGuid":"11111111-1111-1111-1111-111111111111",
              "legionStampName":"legion-test",
              "podType":"FunctionsSplitWorkerPod",
              "app":{"functionAppName":"test-app","functionAppSiteId":42,"antaresStampName":"antares-test"}
            },
            "isDiscoveryPod":false,
            "isAlwaysReady":false,
            "functionGroup":"test-group",
            "configLastModifiedTime":100,
            "contentLastModifiedTime":200
          }
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
        const string proxyEndpoint = "http://worker-pod:28080/";
        await using WorkerProxyWebApplicationFactory factory = CreateHttpCapabilityFactory(proxyEndpoint);
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        await AssertReadinessAsync(management, "/admin/instance/ready", HttpStatusCode.OK, timeout.Token);
        await AssertReadinessAsync(management, "/admin/worker/ready", HttpStatusCode.ServiceUnavailable, timeout.Token);
        using (HttpResponseMessage notReady = await PutManagementJsonAsync(
            management, "/admin/worker/assignment", assignmentBody, timeout.Token))
        {
            await AssertManagementErrorAsync(notReady, HttpStatusCode.ServiceUnavailable, "WorkerNotReady", timeout.Token);
        }

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
            using (JsonDocument unassigned = await ReadManagementStateAsync(management, null, timeout.Token))
            {
                Assert.Equal(2, unassigned.RootElement.GetProperty("revisionId").GetInt64());
                Assert.Equal("None", unassigned.RootElement.GetProperty("workerPodState").GetProperty("podStatus").GetString());
                Assert.False(unassigned.RootElement.GetProperty("workerPodState").TryGetProperty("startupMode", out _));
            }

            Task<HttpResponseMessage> assignmentPoll = management.GetAsync(
                "/admin/worker/state?lastKnownRevision=2", timeout.Token);
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
                Assert.Equal(3, state.RootElement.GetProperty("revisionId").GetInt64());
                JsonElement pod = state.RootElement.GetProperty("workerPodState");
                Assert.Equal("ReadyForRequest", pod.GetProperty("podStatus").GetString());
                Assert.Equal(startupMode, pod.GetProperty("startupMode").GetString());
                Assert.Equal("test-group", pod.GetProperty("functionGroupName").GetString());
                Assert.False(pod.GetProperty("isAlwaysReady").GetBoolean());
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

            Assert.Equal(3, manager.State.Revision);
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
            Assert.Equal(3, manager.State.Revision);

            Task<HttpResponseMessage> terminationPoll = management.GetAsync(
                "/admin/worker/state?lastKnownRevision=3", timeout.Token);
            await WaitForManagementPollAsync(manager, timeout.Token);
            await worker.CompleteRequestAsync(timeout.Token);
            Assert.Equal(StatusCode.Unavailable, await runtime.WaitForTerminationAsync(timeout.Token));
            Assert.Equal(StatusCode.Unavailable, await worker.WaitForTerminationAsync(timeout.Token));
            using (HttpResponseMessage changed = await terminationPoll)
            {
                Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
                Assert.True(changed.Headers.CacheControl?.NoStore);
                using JsonDocument state = JsonDocument.Parse(await changed.Content.ReadAsStringAsync(timeout.Token));
                Assert.Equal(4, state.RootElement.GetProperty("revisionId").GetInt64());
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
            Assert.Equal(4, current.RootElement.GetProperty("revisionId").GetInt64());
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
        await AssertManagementErrorAsync(assignment, HttpStatusCode.ServiceUnavailable, "WorkerNotReady", timeout.Token);
    }

    [Fact]
    public async Task SplitInstanceStateReturnsUnlinkedWorkerAfterStaticAssignment()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await worker.WriteAsync(CreateStartStream(), timeout.Token);
        while (!manager.State.IsWorkerReady)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }

        using (HttpResponseMessage assignment = await PutManagementJsonAsync(
            management, "/admin/worker/assignment", SplitManagementAssignment, timeout.Token))
        {
            Assert.Equal(HttpStatusCode.Created, assignment.StatusCode);
        }

        using JsonDocument state = await ReadSplitManagementStateAsync(management, timeout.Token);
        JsonElement root = state.RootElement;
        Assert.Equal("FunctionsSplitWorkerProxy", root.GetProperty("functionsContainerType").GetString());
        JsonElement staticState = root.GetProperty("staticState");
        JsonElement self = staticState.GetProperty("self");
        Assert.Equal("test-worker-pod", self.GetProperty("podName").GetString());
        Assert.Equal("FunctionsSplitWorkerPod", self.GetProperty("podType").GetString());
        Assert.Equal("test-app", self.GetProperty("app").GetProperty("functionAppName").GetString());
        Assert.False(staticState.GetProperty("isDiscoveryPod").GetBoolean());
        Assert.False(staticState.GetProperty("isAlwaysReady").GetBoolean());
        Assert.Equal("test-group", staticState.GetProperty("functionGroup").GetString());
        Assert.Equal(100, staticState.GetProperty("configLastModifiedTime").GetInt64());
        Assert.Equal(200, staticState.GetProperty("contentLastModifiedTime").GetInt64());
        JsonElement dynamicState = root.GetProperty("dynamicState");
        Assert.Equal(3, dynamicState.GetProperty("revisionId").GetInt64());
        Assert.Equal("Starting", dynamicState.GetProperty("status").GetString());
        JsonElement link = dynamicState.GetProperty("link");
        Assert.Equal("Unlinked", link.GetProperty("state").GetString());
        Assert.False(link.TryGetProperty("runtime", out _));
    }

    [Fact]
    public async Task SplitRuntimeLinkTransitionsPendingToLinkedState()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await worker.WriteAsync(CreateStartStream(), timeout.Token);
        while (!manager.State.IsWorkerReady)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }

        using (HttpResponseMessage assignment = await PutManagementJsonAsync(
            management, "/admin/worker/assignment", SplitManagementAssignment, timeout.Token))
        {
            Assert.Equal(HttpStatusCode.Created, assignment.StatusCode);
        }

        Task<HttpResponseMessage> pendingPoll = management.GetAsync($"{InfraStatePath}?lastKnownRevision=3", timeout.Token);
        await WaitForManagementPollAsync(manager, timeout.Token);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        StreamingMessage init = new()
        {
            RequestId = "runtime-init",
            WorkerInitRequest = new() { HostVersion = "test-host" }
        };
        await runtime.WriteAsync(init, timeout.Token);
        Assert.Equal(init, await worker.ReadAsync(timeout.Token));
        Assert.Equal("test-worker", (await runtime.ReadAsync(timeout.Token)).StartStream.WorkerId);

        using (HttpResponseMessage pendingResponse = await pendingPoll)
        {
            Assert.Equal(HttpStatusCode.OK, pendingResponse.StatusCode);
            using JsonDocument pending = JsonDocument.Parse(
                await pendingResponse.Content.ReadAsStringAsync(timeout.Token));
            JsonElement dynamicState = pending.RootElement.GetProperty("dynamicState");
            Assert.Equal(4, dynamicState.GetProperty("revisionId").GetInt64());
            Assert.Equal("Starting", dynamicState.GetProperty("status").GetString());
            JsonElement link = dynamicState.GetProperty("link");
            Assert.Equal("Pending", link.GetProperty("state").GetString());
            Assert.False(link.TryGetProperty("runtime", out _));
        }

        Task<HttpResponseMessage> linkedPoll = management.GetAsync($"{InfraStatePath}?lastKnownRevision=4", timeout.Token);
        await WaitForManagementPollAsync(manager, timeout.Token);
        await worker.WriteAsync(CreateInitResponse(init.RequestId, httpUri: null), timeout.Token);
        Assert.NotNull((await runtime.ReadAsync(timeout.Token)).WorkerInitResponse);

        using HttpResponseMessage linkedResponse = await linkedPoll;
        Assert.Equal(HttpStatusCode.OK, linkedResponse.StatusCode);
        using JsonDocument linked = JsonDocument.Parse(await linkedResponse.Content.ReadAsStringAsync(timeout.Token));
        JsonElement linkedDynamicState = linked.RootElement.GetProperty("dynamicState");
        Assert.Equal(5, linkedDynamicState.GetProperty("revisionId").GetInt64());
        Assert.Equal("ReadyForWork", linkedDynamicState.GetProperty("status").GetString());
        JsonElement linkedState = linkedDynamicState.GetProperty("link");
        Assert.Equal("Linked", linkedState.GetProperty("state").GetString());
        Assert.False(linkedState.TryGetProperty("runtime", out _));
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
        while (!manager.State.IsWorkerReady)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }

        using (HttpResponseMessage assignment = await PutManagementJsonAsync(
            management, "/admin/worker/assignment", SplitManagementAssignment, timeout.Token))
        {
            Assert.Equal(HttpStatusCode.Created, assignment.StatusCode);
        }

        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        StreamingMessage init = new()
        {
            RequestId = "runtime-init",
            WorkerInitRequest = new() { HostVersion = "test-host" }
        };
        await runtime.WriteAsync(init, timeout.Token);
        Assert.Equal(init, await worker.ReadAsync(timeout.Token));

        await using RelayClient conflictingRuntime =
            CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        Grpc.Core.RpcException exception =
            await conflictingRuntime.WriteAndReadRejectionAsync(CreateMessage("conflict"), timeout.Token);
        Assert.Equal(StatusCode.AlreadyExists, exception.StatusCode);

        using JsonDocument split = await ReadSplitManagementStateAsync(management, timeout.Token);
        JsonElement dynamicState = split.RootElement.GetProperty("dynamicState");
        Assert.Equal(4, dynamicState.GetProperty("revisionId").GetInt64());
        JsonElement link = dynamicState.GetProperty("link");
        Assert.Equal("Pending", link.GetProperty("state").GetString());
        Assert.False(link.TryGetProperty("runtime", out _));

        using JsonDocument legacy = await ReadManagementStateAsync(management, lastKnownRevision: null, timeout.Token);
        Assert.Equal("FunctionsWorkerPod", legacy.RootElement.GetProperty("functionsContainerType").GetString());
        Assert.Equal(4, legacy.RootElement.GetProperty("revisionId").GetInt64());
        Assert.True(legacy.RootElement.TryGetProperty("workerPodState", out _));
        Assert.False(legacy.RootElement.TryGetProperty("staticState", out _));
        Assert.False(legacy.RootElement.TryGetProperty("dynamicState", out _));
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

    private static async Task<JsonDocument> ReadSplitManagementStateAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync(InfraStatePath, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
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
