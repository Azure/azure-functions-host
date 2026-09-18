// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Rpc.Client;
using Moq;
using Xunit;

namespace Azure.Functions.Host.Tests;

/// <summary>
/// Exercises the compute composition, legacy MVC/Newtonsoft pipeline, registry, and real outbound
/// FunctionRpc transport. Fixture authorization is permissive and does not test production authentication.
/// </summary>
public sealed class WorkerLinkEndpointIntegrationTests
{
    private const string WorkerId = "worker-pod-abc123";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public async Task Put_ValidRequest_ReturnsCreatedAndExactRetryReturnsOk(string? retryHttpEndpoint)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        server.AddWorker(WorkerId);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);
        string requestJson = LinkJson(server.Endpoint);

        using HttpResponseMessage response = await host.PutAsync(requestJson, timeout.Token);

        await AssertLinkResponseAsync(response, HttpStatusCode.Created, timeout.Token);
        Assert.Equal(1, server.StreamCount);

        using HttpResponseMessage retry = await host.PutAsync(LinkJson(server.Endpoint, retryHttpEndpoint), timeout.Token);
        await AssertLinkResponseAsync(retry, HttpStatusCode.OK, timeout.Token);
        Assert.Equal(1, server.StreamCount);
    }

    [Fact]
    public async Task Put_PendingInitialization_WaitsBeforeReturningCreated()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        var worker = server.AddPendingWorker(WorkerId);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);

        var request = host.BeginPut(LinkJson(server.Endpoint), timeout.Token);
        await request.WaitForProgressAsync(worker.InitializationStarted, timeout.Token);

        Assert.False(request.Response.IsCompleted);

        worker.CompleteInitialization();
        using HttpResponseMessage response = await request.Response.WaitAsync(timeout.Token);
        await AssertLinkResponseAsync(response, HttpStatusCode.Created, timeout.Token);
    }

    [Theory]
    [InlineData(1, null, null)]
    [InlineData(8, null, null)]
    [InlineData(1, null, "")]
    [InlineData(1, null, " \t ")]
    [InlineData(1, "", null)]
    [InlineData(1, "", "")]
    [InlineData(1, "", " \t ")]
    [InlineData(1, " \t ", null)]
    [InlineData(1, " \t ", "")]
    [InlineData(1, " \t ", " \t ")]
    [InlineData(1, "http://worker-proxy:28080", "http://worker-proxy:28080/")]
    [InlineData(8, "http://worker-proxy", "HTTP://WORKER-PROXY:80/")]
    [InlineData(1, "https://worker-proxy", "HTTPS://WORKER-PROXY:443/")]
    public async Task Put_ConcurrentExactRetries_ReturnCreatedOnlyToOriginal(int retryCount, string? httpEndpoint, string? retryHttpEndpoint)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        var worker = server.AddPendingWorker(WorkerId);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);
        string requestJson = LinkJson(server.Endpoint, httpEndpoint);
        string retryJson = JsonSerializer.Serialize(new
        {
            workerGrpcEndpoint = server.Endpoint.AbsoluteUri,
            workerHttpEndpoint = retryHttpEndpoint,
            workerContainerEncryptionKey = "ignored-retry-key",
        });

        var original = host.BeginPut(requestJson, timeout.Token);
        await original.WaitForProgressAsync(worker.InitializationStarted, timeout.Token);
        WorkerLinkTestHost.PendingRequest[] retries = [.. Enumerable.Range(0, retryCount)
            .Select(_ => host.BeginPut(retryJson, timeout.Token))];
        await Task.WhenAll(retries.Select(retry => retry.WaitForProgressAsync(retry.ActionInvoked, timeout.Token)));

        Assert.False(original.Response.IsCompleted);
        Assert.All(retries, retry => Assert.False(retry.Response.IsCompleted));
        Assert.Equal(1, server.StreamCount);

        worker.CompleteInitialization();
        using HttpResponseMessage originalResponse = await original.Response.WaitAsync(timeout.Token);
        await AssertLinkResponseAsync(originalResponse, HttpStatusCode.Created, timeout.Token);
        foreach (WorkerLinkTestHost.PendingRequest retry in retries)
        {
            using HttpResponseMessage retryResponse = await retry.Response.WaitAsync(timeout.Token);
            await AssertLinkResponseAsync(retryResponse, HttpStatusCode.OK, timeout.Token);
        }

        using HttpResponseMessage completedReplay = await host.PutAsync(
            string.IsNullOrWhiteSpace(httpEndpoint) ? LinkJson(server.Endpoint) : retryJson, timeout.Token);
        await AssertLinkResponseAsync(completedReplay, HttpStatusCode.OK, timeout.Token);
        Assert.Equal(1, server.StreamCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Put_DifferentWorkers_LinkIndependentlyAndReplayTheirOwnStreams(bool shareEndpoint)
    {
        const string secondWorkerId = "worker-pod-def456";
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using FakeWorkerProxyGrpcServer firstServer = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        await using FakeWorkerProxyGrpcServer otherEndpoint = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        FakeWorkerProxyGrpcServer secondServer = shareEndpoint ? firstServer : otherEndpoint;
        var firstWorker = firstServer.AddPendingWorker(WorkerId);
        var secondWorker = secondServer.AddPendingWorker(secondWorkerId);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);

        var first = host.BeginPut(LinkJson(firstServer.Endpoint), timeout.Token);
        await first.WaitForProgressAsync(firstWorker.InitializationStarted, timeout.Token);
        var second = host.BeginPut(LinkJson(secondServer.Endpoint), timeout.Token, secondWorkerId);
        await second.WaitForProgressAsync(secondWorker.InitializationStarted, timeout.Token);

        Assert.False(first.Response.IsCompleted);
        Assert.False(second.Response.IsCompleted);
        Assert.Equal(2, firstServer.StreamCount + otherEndpoint.StreamCount);

        secondWorker.CompleteInitialization();
        using HttpResponseMessage secondResponse = await second.Response.WaitAsync(timeout.Token);
        await AssertLinkResponseAsync(secondResponse, HttpStatusCode.Created, timeout.Token);
        Assert.False(first.Response.IsCompleted);

        firstWorker.CompleteInitialization();
        using HttpResponseMessage firstResponse = await first.Response.WaitAsync(timeout.Token);
        await AssertLinkResponseAsync(firstResponse, HttpStatusCode.Created, timeout.Token);
        using HttpResponseMessage firstReplay = await host.PutAsync(LinkJson(firstServer.Endpoint), timeout.Token);
        using HttpResponseMessage secondReplay = await host.PutAsync(LinkJson(secondServer.Endpoint), timeout.Token, secondWorkerId);
        await AssertLinkResponseAsync(firstReplay, HttpStatusCode.OK, timeout.Token);
        await AssertLinkResponseAsync(secondReplay, HttpStatusCode.OK, timeout.Token);
        Assert.Equal(shareEndpoint ? 2 : 1, firstServer.StreamCount);
        Assert.Equal(shareEndpoint ? 0 : 1, otherEndpoint.StreamCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Put_RejectedInitialization_FailsWaitersAndAllowsRetry(bool concurrentRetry)
    {
        const string privateWorkerError = "private-worker-path-and-secret-do-not-return";
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        var worker = server.AddPendingWorker(WorkerId);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);
        string requestJson = LinkJson(server.Endpoint);

        var request = host.BeginPut(requestJson, timeout.Token);
        await request.WaitForProgressAsync(worker.InitializationStarted, timeout.Token);
        WorkerLinkTestHost.PendingRequest? duplicate = null;
        if (concurrentRetry)
        {
            duplicate = host.BeginPut(requestJson, timeout.Token);
            await duplicate.WaitForProgressAsync(duplicate.ActionInvoked, timeout.Token);
            Assert.False(duplicate.Response.IsCompleted);
        }

        Assert.Equal(1, server.StreamCount);
        worker.FailInitialization(privateWorkerError);

        using HttpResponseMessage failure = await request.Response.WaitAsync(timeout.Token);
        await AssertLinkErrorAsync(failure, HttpStatusCode.ServiceUnavailable, "WorkerUnavailable", timeout.Token);
        Assert.DoesNotContain(privateWorkerError, await failure.Content.ReadAsStringAsync(timeout.Token), StringComparison.Ordinal);
        if (duplicate is not null)
        {
            using HttpResponseMessage duplicateFailure = await duplicate.Response.WaitAsync(timeout.Token);
            await AssertLinkErrorAsync(duplicateFailure, HttpStatusCode.ServiceUnavailable, "WorkerUnavailable", timeout.Token);
            Assert.DoesNotContain(privateWorkerError, await duplicateFailure.Content.ReadAsStringAsync(timeout.Token), StringComparison.Ordinal);
        }

        await worker.Disconnected.WaitAsync(timeout.Token);

        server.AddWorker(WorkerId);
        using HttpResponseMessage retry = await host.PutAsync(requestJson, timeout.Token);

        await AssertLinkResponseAsync(retry, HttpStatusCode.Created, timeout.Token);
        Assert.Equal(2, server.StreamCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    [InlineData("http://worker-proxy:28080")]
    public async Task Put_InvalidWorkerHttpUri_IsIgnored(string? httpEndpoint)
    {
        const string invalidHttpUri = "not-an-absolute-uri";
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        server.AddWorker(WorkerId, httpUri: invalidHttpUri);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);
        string requestJson = JsonSerializer.Serialize(new
        {
            workerGrpcEndpoint = server.Endpoint.AbsoluteUri,
            workerHttpEndpoint = httpEndpoint,
        });

        using HttpResponseMessage linked = await host.PutAsync(requestJson, timeout.Token);

        await AssertLinkResponseAsync(linked, HttpStatusCode.Created, timeout.Token);
        Assert.DoesNotContain(invalidHttpUri, await linked.Content.ReadAsStringAsync(timeout.Token), StringComparison.Ordinal);
        using HttpResponseMessage retry = await host.PutAsync(requestJson, timeout.Token);

        await AssertLinkResponseAsync(retry, HttpStatusCode.OK, timeout.Token);
        Assert.Equal(1, server.StreamCount);
    }

    [Fact]
    public async Task Put_SameWorkerWithConflictingEndpoint_ReturnsConflictWithoutDialing()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        await using FakeWorkerProxyGrpcServer otherEndpoint = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        var worker = server.AddPendingWorker(WorkerId);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);

        var original = host.BeginPut(LinkJson(server.Endpoint), timeout.Token);
        await original.WaitForProgressAsync(worker.InitializationStarted, timeout.Token);
        string conflictJson = LinkJson(otherEndpoint.Endpoint);

        using HttpResponseMessage pendingConflict = await host.PutAsync(conflictJson, timeout.Token);
        await AssertLinkErrorAsync(pendingConflict, HttpStatusCode.Conflict, "LinkConflict", timeout.Token);
        Assert.False(original.Response.IsCompleted);
        Assert.Equal(1, server.StreamCount);
        Assert.Equal(0, otherEndpoint.StreamCount);

        worker.CompleteInitialization();
        using HttpResponseMessage linked = await original.Response.WaitAsync(timeout.Token);
        await AssertLinkResponseAsync(linked, HttpStatusCode.Created, timeout.Token);

        using HttpResponseMessage linkedConflict = await host.PutAsync(conflictJson, timeout.Token);
        await AssertLinkErrorAsync(linkedConflict, HttpStatusCode.Conflict, "LinkConflict", timeout.Token);
        Assert.Equal(1, server.StreamCount);
        Assert.Equal(0, otherEndpoint.StreamCount);
    }

    [Theory]
    [InlineData(null, "http://worker-proxy:28080")]
    [InlineData("", "http://worker-proxy:28080")]
    [InlineData(" \t ", "http://worker-proxy:28080")]
    [InlineData("http://worker-proxy:28080", null)]
    [InlineData("http://worker-proxy:28080", "")]
    [InlineData("http://worker-proxy:28080", " \t ")]
    [InlineData("http://worker-proxy:28080", "http://other-proxy:28080")]
    [InlineData("http://worker-proxy:28080", "https://worker-proxy:28080")]
    [InlineData("http://worker-proxy:28080", "http://worker-proxy:28081")]
    public async Task Put_ConflictingHttpEndpoint_ReturnsConflictWithoutChangingLink(string? httpEndpoint, string? retryHttpEndpoint)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        var worker = server.AddPendingWorker(WorkerId);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);
        string originalJson = LinkJson(server.Endpoint, httpEndpoint);
        string conflictJson = LinkJson(server.Endpoint, retryHttpEndpoint);

        var original = host.BeginPut(originalJson, timeout.Token);
        await original.WaitForProgressAsync(worker.InitializationStarted, timeout.Token);
        using HttpResponseMessage pendingConflict = await host.PutAsync(conflictJson, timeout.Token);
        await AssertLinkErrorAsync(pendingConflict, HttpStatusCode.Conflict, "LinkConflict", timeout.Token);
        Assert.False(original.Response.IsCompleted);
        Assert.Equal(1, server.StreamCount);

        worker.CompleteInitialization();
        using HttpResponseMessage created = await original.Response.WaitAsync(timeout.Token);
        await AssertLinkResponseAsync(created, HttpStatusCode.Created, timeout.Token);
        using HttpResponseMessage readyConflict = await host.PutAsync(conflictJson, timeout.Token);
        await AssertLinkErrorAsync(readyConflict, HttpStatusCode.Conflict, "LinkConflict", timeout.Token);
        if (!string.IsNullOrWhiteSpace(httpEndpoint))
        {
            using HttpResponseMessage omittedEndpoint = await host.PutAsync(LinkJson(server.Endpoint), timeout.Token);
            await AssertLinkErrorAsync(omittedEndpoint, HttpStatusCode.Conflict, "LinkConflict", timeout.Token);
        }

        using HttpResponseMessage replay = await host.PutAsync(originalJson, timeout.Token);
        await AssertLinkResponseAsync(replay, HttpStatusCode.OK, timeout.Token);
        Assert.Equal(1, server.StreamCount);
    }

    [Fact]
    public async Task Put_CanceledInitialization_CleansAttemptAndAllowsRetry()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        var worker = server.AddPendingWorker(WorkerId);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);
        string requestJson = LinkJson(server.Endpoint);

        var request = host.BeginPut(requestJson, cancellation.Token);
        await request.WaitForProgressAsync(worker.InitializationStarted, timeout.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.Response.WaitAsync(timeout.Token));
        await request.ActionCompleted.WaitAsync(timeout.Token);
        await worker.Disconnected.WaitAsync(timeout.Token);

        server.AddWorker(WorkerId);
        using HttpResponseMessage retry = await host.PutAsync(requestJson, timeout.Token);

        await AssertLinkResponseAsync(retry, HttpStatusCode.Created, timeout.Token);
        Assert.Equal(2, server.StreamCount);
    }

    [Fact]
    public async Task Put_CanceledExactRetry_DoesNotCancelOriginalInitialization()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        var worker = server.AddPendingWorker(WorkerId);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);
        string requestJson = LinkJson(server.Endpoint);

        var original = host.BeginPut(requestJson, timeout.Token);
        await original.WaitForProgressAsync(worker.InitializationStarted, timeout.Token);
        var retry = host.BeginPut(requestJson, cancellation.Token);
        await retry.WaitForProgressAsync(retry.ActionInvoked, timeout.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retry.Response.WaitAsync(timeout.Token));
        await retry.ActionCompleted.WaitAsync(timeout.Token);
        Assert.False(original.Response.IsCompleted);
        Assert.False(worker.Disconnected.IsCompleted);

        worker.CompleteInitialization();
        using HttpResponseMessage response = await original.Response.WaitAsync(timeout.Token);
        await AssertLinkResponseAsync(response, HttpStatusCode.Created, timeout.Token);
        Assert.Equal(1, server.StreamCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{"workerGrpcEndpoint":"{endpoint}","broken":}""")]
    [InlineData("""{"workerHttpEndpoint":"{endpoint}"}""")]
    [InlineData("""{"workerGrpcEndpoint":"relative/path"}""")]
    [InlineData("""{"workerGrpcEndpoint":"ftp://127.0.0.1:5000"}""")]
    [InlineData("""{"workerGrpcEndpoint":"{endpoint}","workerHttpEndpoint":"relative/path"}""")]
    public async Task Put_InvalidRequest_ReturnsBadRequestWithoutDialing(string? json)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);

        using HttpResponseMessage response = await host.PutAsync(
            json?.Replace("{endpoint}", server.Endpoint.AbsoluteUri, StringComparison.Ordinal), timeout.Token);

        await ReadValidationErrorsAsync(response, timeout.Token);
        Assert.Equal(0, server.StreamCount);
    }

    [Fact]
    public async Task Put_EmptyObject_ReturnsRequiredGrpcEndpointError()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);

        using HttpResponseMessage response = await host.PutAsync("{}", timeout.Token);
        JsonElement error = Assert.Single(await ReadValidationErrorsAsync(response, timeout.Token));

        Assert.Equal("Required", error.GetProperty("code").GetString());
        Assert.Equal("workerGrpcEndpoint", error.GetProperty("target").GetString());
        Assert.Equal(0, server.StreamCount);
    }

    [Fact]
    public async Task Put_MultipleInvalidFields_ReturnsAllErrors()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);
        const string json = """
            {"workerGrpcEndpoint":"relative/grpc","workerHttpEndpoint":"relative/http"}
            """;

        using HttpResponseMessage response = await host.PutAsync(json, timeout.Token);
        JsonElement[] errors = await ReadValidationErrorsAsync(response, timeout.Token);

        Assert.Equal(
            [("workerGrpcEndpoint", "InvalidEndpoint"), ("workerHttpEndpoint", "InvalidEndpoint")],
            errors.Select(error => (error.GetProperty("target").GetString(), error.GetProperty("code").GetString())));
        Assert.Equal(0, server.StreamCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"not an object\"")]
    [InlineData("""{"workerContainerEncryptionKey":{"private":"do-not-echo"}}""")]
    [InlineData("""{"workerGrpcEndpoint":""")]
    public async Task Put_UnbindableBody_ReturnsSafeRequestError(string? json)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);

        using HttpResponseMessage response = await host.PutAsync(json, timeout.Token);
        JsonElement error = Assert.Single(await ReadValidationErrorsAsync(response, timeout.Token));

        Assert.Equal("InvalidBody", error.GetProperty("code").GetString());
        Assert.Equal("request", error.GetProperty("target").GetString());
        Assert.DoesNotContain("do-not-echo", await response.Content.ReadAsStringAsync(timeout.Token), StringComparison.Ordinal);
        Assert.Equal(0, server.StreamCount);
    }

    [Theory]
    [InlineData("workerPodName", "\"worker-pod-abc123\"")]
    [InlineData("workerPodName", "\"another-worker\"")]
    [InlineData("workerPodName", "\"\"")]
    [InlineData("workerPodName", "null")]
    [InlineData("workerPodName", "123")]
    [InlineData("workerPodName", "true")]
    [InlineData("workerPodName", "{}")]
    [InlineData("workerPodName", "[]")]
    [InlineData("WorkerPodName", "null")]
    [InlineData("WORKERPODNAME", "\"another-worker\"")]
    public async Task Put_BodyWorkerPodName_IsIgnoredAndUsesRouteIdentity(string propertyName, string identityJson)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        server.AddWorker(WorkerId);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);
        string json = $$"""
            {"workerGrpcEndpoint":"{{server.Endpoint.AbsoluteUri}}","{{propertyName}}":{{identityJson}}}
            """;

        using HttpResponseMessage response = await host.PutAsync(json, timeout.Token);
        await AssertLinkResponseAsync(response, HttpStatusCode.Created, timeout.Token);

        using HttpResponseMessage retryWithBodyIdentity = await host.PutAsync(json, timeout.Token);
        await AssertLinkResponseAsync(retryWithBodyIdentity, HttpStatusCode.OK, timeout.Token);
        using HttpResponseMessage retryWithoutBodyIdentity = await host.PutAsync(LinkJson(server.Endpoint), timeout.Token);
        await AssertLinkResponseAsync(retryWithoutBodyIdentity, HttpStatusCode.OK, timeout.Token);
        Assert.Equal(1, server.StreamCount);
    }

    [Fact]
    public async Task Put_BodyWorkerPodName_DoesNotBypassEndpointConflict()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        await using FakeWorkerProxyGrpcServer otherEndpoint = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        server.AddWorker(WorkerId);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);
        using HttpResponseMessage linked = await host.PutAsync(LinkJson(server.Endpoint), timeout.Token);
        await AssertLinkResponseAsync(linked, HttpStatusCode.Created, timeout.Token);
        string conflictJson = JsonSerializer.Serialize(new
        {
            workerPodName = "another-worker",
            workerGrpcEndpoint = otherEndpoint.Endpoint.AbsoluteUri,
        });

        using HttpResponseMessage rejected = await host.PutAsync(conflictJson, timeout.Token);
        await AssertLinkErrorAsync(rejected, HttpStatusCode.Conflict, "LinkConflict", timeout.Token);
        using HttpResponseMessage replay = await host.PutAsync(LinkJson(server.Endpoint), timeout.Token);

        await AssertLinkResponseAsync(replay, HttpStatusCode.OK, timeout.Token);
        Assert.Equal(1, server.StreamCount);
        Assert.Equal(0, otherEndpoint.StreamCount);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    [InlineData("\u00a0\u2003")]
    public async Task Put_BlankWorkerIdentity_LeavesExistingLinkUnchanged(string workerPodName)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        server.AddWorker(WorkerId);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);
        string json = LinkJson(server.Endpoint);
        using HttpResponseMessage linked = await host.PutAsync(json, timeout.Token);
        await AssertLinkResponseAsync(linked, HttpStatusCode.Created, timeout.Token);

        using HttpResponseMessage rejected = await host.PutAsync(json, timeout.Token, Uri.EscapeDataString(workerPodName));
        JsonElement error = Assert.Single(await ReadValidationErrorsAsync(rejected, timeout.Token));
        Assert.Equal("Required", error.GetProperty("code").GetString());
        Assert.Equal("workerPodName", error.GetProperty("target").GetString());
        Assert.Equal(1, server.StreamCount);

        using HttpResponseMessage replay = await host.PutAsync(json, timeout.Token);
        await AssertLinkResponseAsync(replay, HttpStatusCode.OK, timeout.Token);
        Assert.Equal(1, server.StreamCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(253)]
    [InlineData(254)]
    [InlineData(1024)]
    public async Task Put_WorkerIdentityLength_DoesNotRestrictLinking(int length)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        string workerPodName = new('a', length);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        server.AddWorker(workerPodName);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);

        using HttpResponseMessage response = await host.PutAsync(LinkJson(server.Endpoint), timeout.Token, workerPodName);

        await AssertLinkResponseAsync(response, HttpStatusCode.Created, timeout.Token);
        Assert.Equal(1, server.StreamCount);
    }

    [Theory]
    [InlineData("Worker-Pod")]
    [InlineData("w\u00f6rker")]
    [InlineData("worker_pod")]
    [InlineData("-worker-")]
    [InlineData("worker..pod")]
    [InlineData(" worker-pod")]
    [InlineData("worker-pod ")]
    [InlineData("worker pod")]
    [InlineData("worker\tpod")]
    [InlineData("worker\r\npod")]
    [InlineData("worker\u00a0pod")]
    [InlineData("worker\u007fpod")]
    public async Task Put_OpaqueWorkerIdentity_PreservesIdentityAndAllowsRetry(string workerPodName)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        server.AddWorker(workerPodName);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);
        string json = LinkJson(server.Endpoint);
        string routeIdentity = Uri.EscapeDataString(workerPodName);

        using HttpResponseMessage linked = await host.PutAsync(json, timeout.Token, routeIdentity);
        await AssertLinkResponseAsync(linked, HttpStatusCode.Created, timeout.Token);
        using HttpResponseMessage replay = await host.PutAsync(json, timeout.Token, routeIdentity);
        await AssertLinkResponseAsync(replay, HttpStatusCode.OK, timeout.Token);
        Assert.Equal(1, server.StreamCount);
    }

    [Theory]
    [InlineData("/admin/workers")]
    [InlineData("/admin/workers/worker-pod-abc123/extra")]
    public async Task Put_MissingOrExtraIdentityPathSegment_DoesNotMatch(string path)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);

        using HttpResponseMessage response = await HttpClientJsonExtensions.PutAsJsonAsync(host.Client, path, new
        {
            workerPodName = WorkerId,
            workerGrpcEndpoint = server.Endpoint.AbsoluteUri,
        }, timeout.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, server.StreamCount);
    }

    [Fact]
    public async Task Get_WorkersRoute_DoesNotMatch()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);

        using HttpResponseMessage response = await host.Client.GetAsync($"/admin/workers/{WorkerId}", timeout.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_WithoutComputeComposition_DoesNotExposeWorkerRoute()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, includeCompute: false);

        using HttpResponseMessage response = await host.PutAsync(LinkJson(server.Endpoint), timeout.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, server.StreamCount);
    }

    [Theory]
    [InlineData(WorkerLinkFailureReason.Conflict, HttpStatusCode.Conflict, "LinkConflict")]
    [InlineData(WorkerLinkFailureReason.WorkerTerminated, HttpStatusCode.Conflict, "WorkerTerminated")]
    [InlineData(WorkerLinkFailureReason.RuntimeStopping, HttpStatusCode.ServiceUnavailable, "RuntimeStopping")]
    [InlineData(WorkerLinkFailureReason.Unavailable, HttpStatusCode.ServiceUnavailable, "WorkerUnavailable")]
    [InlineData(WorkerLinkFailureReason.Timeout, HttpStatusCode.ServiceUnavailable, "LinkTimeout")]
    public async Task Put_LinkFailure_ReturnsExactErrorEnvelope(WorkerLinkFailureReason reason, HttpStatusCode status, string code)
    {
        const string privateDiagnostic = "private-worker-detail-do-not-return";
        using var timeout = new CancellationTokenSource(TestTimeout);
        Uri endpoint = new("http://worker-proxy:50053");
        Mock<IWorkerChannelRegistry> registry = new();
        registry.Setup(value => value.LinkAsync(WorkerId, endpoint, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WorkerLinkException(reason, privateDiagnostic, new Exception(privateDiagnostic)));
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, registry: registry.Object);

        using HttpResponseMessage response = await host.PutAsync(LinkJson(endpoint), timeout.Token);

        await AssertLinkErrorAsync(response, status, code, timeout.Token);
        Assert.DoesNotContain(privateDiagnostic, await response.Content.ReadAsStringAsync(timeout.Token), StringComparison.Ordinal);
        registry.Verify(value => value.LinkAsync(WorkerId, endpoint, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Put_InitializationDeadline_ReturnsLinkTimeoutToAllWaitersAndAllowsRetry()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        var worker = server.AddPendingWorker(WorkerId);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, linkTimeout: TimeSpan.FromSeconds(2));
        string json = LinkJson(server.Endpoint);
        var original = host.BeginPut(json, timeout.Token);
        await original.WaitForProgressAsync(worker.InitializationStarted, timeout.Token);
        var retry = host.BeginPut(json, timeout.Token);
        await retry.WaitForProgressAsync(retry.ActionInvoked, timeout.Token);
        Assert.False(original.Response.IsCompleted);
        Assert.False(retry.Response.IsCompleted);

        using HttpResponseMessage originalResponse = await original.Response.WaitAsync(timeout.Token);
        using HttpResponseMessage retryResponse = await retry.Response.WaitAsync(timeout.Token);

        await AssertLinkErrorAsync(originalResponse, HttpStatusCode.ServiceUnavailable, "LinkTimeout", timeout.Token);
        await AssertLinkErrorAsync(retryResponse, HttpStatusCode.ServiceUnavailable, "LinkTimeout", timeout.Token);
        await worker.Disconnected.WaitAsync(timeout.Token);
        Assert.Equal(1, server.StreamCount);

        server.AddWorker(WorkerId);
        using HttpResponseMessage created = await host.PutAsync(json, timeout.Token);
        await AssertLinkResponseAsync(created, HttpStatusCode.Created, timeout.Token);
        using HttpResponseMessage replay = await host.PutAsync(json, timeout.Token);
        await AssertLinkResponseAsync(replay, HttpStatusCode.OK, timeout.Token);
        Assert.Equal(2, server.StreamCount);
    }

    private static string LinkJson(Uri endpoint)
        => JsonSerializer.Serialize(new { workerGrpcEndpoint = endpoint.AbsoluteUri });

    private static string LinkJson(Uri endpoint, string? httpEndpoint)
        => JsonSerializer.Serialize(new { workerGrpcEndpoint = endpoint.AbsoluteUri, workerHttpEndpoint = httpEndpoint });

    private static async Task<JsonElement[]> ReadValidationErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        JsonElement body = document.RootElement;
        Assert.Equal(JsonValueKind.Object, body.ValueKind);
        Assert.Equal("errors", Assert.Single(body.EnumerateObject()).Name);
        JsonElement errorArray = body.GetProperty("errors");
        Assert.Equal(JsonValueKind.Array, errorArray.ValueKind);
        JsonElement[] errors = errorArray.EnumerateArray().Select(error => error.Clone()).ToArray();
        Assert.NotEmpty(errors);
        Assert.All(errors, error =>
        {
            Assert.Equal(JsonValueKind.Object, error.ValueKind);
            Assert.Equal(new[] { "code", "target" },
                error.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
            Assert.Contains(error.GetProperty("code").GetString(), new[] { "Required", "InvalidEndpoint", "InvalidBody" });
            Assert.Contains(error.GetProperty("target").GetString(), new[] { "request", "workerPodName", "workerGrpcEndpoint", "workerHttpEndpoint" });
        });
        return errors;
    }

    private static async Task AssertLinkResponseAsync(
        HttpResponseMessage response, HttpStatusCode statusCode, CancellationToken cancellationToken)
    {
        Assert.Equal(statusCode, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private static async Task AssertLinkErrorAsync(
        HttpResponseMessage response, HttpStatusCode statusCode, string code, CancellationToken cancellationToken)
    {
        Assert.Equal(statusCode, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        JsonElement body = document.RootElement;
        Assert.Equal("error", Assert.Single(body.EnumerateObject()).Name);
        JsonElement error = body.GetProperty("error");
        Assert.Equal(new[] { "code", "detail" }, error.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(JsonValueKind.String, error.GetProperty("code").ValueKind);
        Assert.Equal(code, error.GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.String, error.GetProperty("detail").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("detail").GetString()));
    }
}
