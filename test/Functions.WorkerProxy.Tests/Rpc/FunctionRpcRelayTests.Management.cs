// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
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
    private const string ManagementAssignment = """
        {"functionAppName":"test-app","functionGroupName":"test-group","isAlwaysReady":false,
         "environment":{"B":"private-value","A":"1"},"functionAppDirectory":"/home/site/wwwroot"}
        """;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ManagementApis_ObserveNormalStartupAssignmentAndTerminalFailure(bool runtimeFirst)
    {
        const string proxyEndpoint = "http://worker-pod:28080/";
        await using WorkerProxyWebApplicationFactory factory = CreateHttpCapabilityFactory(proxyEndpoint);
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        await AssertReadinessAsync(management, "/admin/instance/ready", HttpStatusCode.OK, timeout.Token);
        await AssertReadinessAsync(management, "/admin/worker/ready", HttpStatusCode.ServiceUnavailable, timeout.Token);
        using (HttpResponseMessage notReady = await PostManagementJsonAsync(
            management, "/admin/worker/assign", ManagementAssignment, timeout.Token))
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
            using (JsonDocument unassigned = await ReadManagementStateAsync(management, "{}", timeout.Token))
            {
                Assert.Equal(2, unassigned.RootElement.GetProperty("revisionId").GetInt64());
                Assert.Equal("None", unassigned.RootElement.GetProperty("workerPodState").GetProperty("podStatus").GetString());
            }

            Task<HttpResponseMessage> assignmentPoll = PostManagementJsonAsync(
                management, "/admin/infra/instanceState", "{\"lastKnownRevision\":2}", timeout.Token);
            await WaitForManagementPollAsync(manager, timeout.Token);
            using (HttpResponseMessage assignment = await PostManagementJsonAsync(
                management, "/admin/worker/assign", ManagementAssignment, timeout.Token))
            {
                Assert.Equal(HttpStatusCode.OK, assignment.StatusCode);
                Assert.Empty(await assignment.Content.ReadAsByteArrayAsync(timeout.Token));
            }

            using (HttpResponseMessage changed = await assignmentPoll)
            {
                Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
                string body = await changed.Content.ReadAsStringAsync(timeout.Token);
                using JsonDocument state = JsonDocument.Parse(body);
                Assert.Equal("FunctionsWorkerPod", state.RootElement.GetProperty("functionsContainerType").GetString());
                Assert.Equal("test-worker-pod", state.RootElement.GetProperty("podName").GetString());
                Assert.Equal(3, state.RootElement.GetProperty("revisionId").GetInt64());
                JsonElement pod = state.RootElement.GetProperty("workerPodState");
                Assert.Equal("ReadyForRequest", pod.GetProperty("podStatus").GetString());
                Assert.Equal("test-group", pod.GetProperty("functionGroupName").GetString());
                Assert.False(pod.GetProperty("isAlwaysReady").GetBoolean());
                Assert.DoesNotContain("private-value", body);
                Assert.DoesNotContain("environment", body);
                Assert.DoesNotContain("runtimePodName", body);
            }

            string replayBody = ManagementAssignment.Replace(
                "\"B\":\"private-value\",\"A\":\"1\"", "\"A\":\"1\",\"B\":\"private-value\"", StringComparison.Ordinal);
            using (HttpResponseMessage replay = await PostManagementJsonAsync(
                management, "/admin/worker/assign", replayBody, timeout.Token))
            {
                Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
                Assert.Empty(await replay.Content.ReadAsByteArrayAsync(timeout.Token));
            }

            string conflictingBody = ManagementAssignment.Replace("test-group", "other-group", StringComparison.Ordinal);
            using (HttpResponseMessage conflict = await PostManagementJsonAsync(
                management, "/admin/worker/assign", conflictingBody, timeout.Token))
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
            Assert.Equal(init, await worker.ReadAsync(timeout.Token));
            StreamingMessage initialized = CreateInitResponse(init.RequestId, "http://localhost:1234/");
            StreamingMessage expected = initialized.Clone();
            expected.WorkerInitResponse.Capabilities["HttpUri"] = proxyEndpoint;
            await worker.WriteAsync(initialized, timeout.Token);
            Assert.Equal(expected, await runtime.ReadAsync(timeout.Token));
            Assert.Equal(3, manager.State.Revision);

            Task<HttpResponseMessage> terminationPoll = PostManagementJsonAsync(
                management, "/admin/infra/instanceState", "{\"lastKnownRevision\":3}", timeout.Token);
            await WaitForManagementPollAsync(manager, timeout.Token);
            await worker.CompleteRequestAsync(timeout.Token);
            Assert.Equal(StatusCode.Unavailable, await runtime.WaitForTerminationAsync(timeout.Token));
            Assert.Equal(StatusCode.Unavailable, await worker.WaitForTerminationAsync(timeout.Token));
            using (HttpResponseMessage changed = await terminationPoll)
            {
                Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
                using JsonDocument state = JsonDocument.Parse(await changed.Content.ReadAsStringAsync(timeout.Token));
                Assert.Equal(4, state.RootElement.GetProperty("revisionId").GetInt64());
                JsonElement pod = state.RootElement.GetProperty("workerPodState");
                Assert.Equal("None", pod.GetProperty("podStatus").GetString());
                Assert.Equal("test-group", pod.GetProperty("functionGroupName").GetString());
                Assert.False(pod.GetProperty("isAlwaysReady").GetBoolean());
            }

            await AssertReadinessAsync(management, "/admin/worker/ready", HttpStatusCode.ServiceUnavailable, timeout.Token);
            await AssertReadinessAsync(management, "/admin/instance/ready", HttpStatusCode.OK, timeout.Token);
            using HttpResponseMessage terminalReplay = await PostManagementJsonAsync(
                management, "/admin/worker/assign", ManagementAssignment, timeout.Token);
            await AssertManagementErrorAsync(terminalReplay, HttpStatusCode.ServiceUnavailable, "WorkerTerminated", timeout.Token);
            using JsonDocument current = await ReadManagementStateAsync(management, "{\"lastKnownRevision\":2}", timeout.Token);
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
        using HttpResponseMessage assignment = await PostManagementJsonAsync(
            management, "/admin/worker/assign", ManagementAssignment, timeout.Token);
        await AssertManagementErrorAsync(assignment, HttpStatusCode.ServiceUnavailable, "WorkerNotReady", timeout.Token);
    }

    private static async Task<HttpResponseMessage> PostManagementJsonAsync(
        HttpClient client, string path, string body, CancellationToken cancellationToken)
    {
        using StringContent content = new(body, Encoding.UTF8, "application/json");
        return await client.PostAsync(path, content, cancellationToken);
    }

    private static async Task<JsonDocument> ReadManagementStateAsync(
        HttpClient client, string body, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await PostManagementJsonAsync(
            client, "/admin/infra/instanceState", body, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private static async Task AssertReadinessAsync(
        HttpClient client, string path, HttpStatusCode statusCode, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync(path, cancellationToken);
        Assert.Equal(statusCode, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(cancellationToken));
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
