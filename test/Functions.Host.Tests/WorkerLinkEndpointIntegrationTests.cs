// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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

    [Fact]
    public async Task Put_ValidRequest_ReturnsLinkedAndExactRetryReusesStream()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        server.AddWorker(WorkerId);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);
        string requestJson = LinkJson(server.Endpoint);

        using HttpResponseMessage response = await host.PutAsync(requestJson, timeout.Token);

        await AssertLinkResponseAsync(response, HttpStatusCode.OK, WorkerId, "Linked", timeout.Token);
        Assert.Equal(1, server.StreamCount);

        using HttpResponseMessage retry = await host.PutAsync(requestJson, timeout.Token);
        await AssertLinkResponseAsync(retry, HttpStatusCode.OK, WorkerId, "Linked", timeout.Token);
        Assert.Equal(1, server.StreamCount);
    }

    [Fact]
    public async Task Put_PendingInitialization_WaitsBeforeReturningLinked()
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
        await AssertLinkResponseAsync(response, HttpStatusCode.OK, WorkerId, "Linked", timeout.Token);
    }

    [Fact]
    public async Task Put_ConcurrentExactRetry_SharesPendingInitialization()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        var worker = server.AddPendingWorker(WorkerId);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);
        string requestJson = LinkJson(server.Endpoint);

        var original = host.BeginPut(requestJson, timeout.Token);
        await original.WaitForProgressAsync(worker.InitializationStarted, timeout.Token);
        var retry = host.BeginPut(requestJson, timeout.Token);
        await retry.WaitForProgressAsync(retry.ActionInvoked, timeout.Token);

        Assert.False(original.Response.IsCompleted);
        Assert.False(retry.Response.IsCompleted);
        Assert.Equal(1, server.StreamCount);

        worker.CompleteInitialization();
        using HttpResponseMessage originalResponse = await original.Response.WaitAsync(timeout.Token);
        using HttpResponseMessage retryResponse = await retry.Response.WaitAsync(timeout.Token);
        await AssertLinkResponseAsync(originalResponse, HttpStatusCode.OK, WorkerId, "Linked", timeout.Token);
        await AssertLinkResponseAsync(retryResponse, HttpStatusCode.OK, WorkerId, "Linked", timeout.Token);
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
        var second = host.BeginPut(LinkJson(secondServer.Endpoint, secondWorkerId), timeout.Token);
        await second.WaitForProgressAsync(secondWorker.InitializationStarted, timeout.Token);

        Assert.False(first.Response.IsCompleted);
        Assert.False(second.Response.IsCompleted);
        Assert.Equal(2, firstServer.StreamCount + otherEndpoint.StreamCount);

        secondWorker.CompleteInitialization();
        using HttpResponseMessage secondResponse = await second.Response.WaitAsync(timeout.Token);
        await AssertLinkResponseAsync(secondResponse, HttpStatusCode.OK, secondWorkerId, "Linked", timeout.Token);
        Assert.False(first.Response.IsCompleted);

        firstWorker.CompleteInitialization();
        using HttpResponseMessage firstResponse = await first.Response.WaitAsync(timeout.Token);
        await AssertLinkResponseAsync(firstResponse, HttpStatusCode.OK, WorkerId, "Linked", timeout.Token);
        using HttpResponseMessage firstReplay = await host.PutAsync(LinkJson(firstServer.Endpoint), timeout.Token);
        using HttpResponseMessage secondReplay = await host.PutAsync(LinkJson(secondServer.Endpoint, secondWorkerId), timeout.Token);
        await AssertLinkResponseAsync(firstReplay, HttpStatusCode.OK, WorkerId, "Linked", timeout.Token);
        await AssertLinkResponseAsync(secondReplay, HttpStatusCode.OK, secondWorkerId, "Linked", timeout.Token);
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
        await AssertLinkResponseAsync(failure, HttpStatusCode.ServiceUnavailable, WorkerId, "LinkFailed", timeout.Token);
        Assert.DoesNotContain(privateWorkerError, await failure.Content.ReadAsStringAsync(timeout.Token), StringComparison.Ordinal);
        if (duplicate is not null)
        {
            using HttpResponseMessage duplicateFailure = await duplicate.Response.WaitAsync(timeout.Token);
            await AssertLinkResponseAsync(duplicateFailure, HttpStatusCode.ServiceUnavailable, WorkerId, "LinkFailed", timeout.Token);
            Assert.DoesNotContain(privateWorkerError, await duplicateFailure.Content.ReadAsStringAsync(timeout.Token), StringComparison.Ordinal);
        }

        await worker.Disconnected.WaitAsync(timeout.Token);

        server.AddWorker(WorkerId);
        using HttpResponseMessage retry = await host.PutAsync(requestJson, timeout.Token);

        await AssertLinkResponseAsync(retry, HttpStatusCode.OK, WorkerId, "Linked", timeout.Token);
        Assert.Equal(2, server.StreamCount);
    }

    [Fact]
    public async Task Put_InvalidWorkerHttpUri_ReturnsUnavailableAndAllowsRetry()
    {
        const string invalidHttpUri = "not-an-absolute-uri";
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        var worker = server.AddWorker(WorkerId, httpUri: invalidHttpUri);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);
        string requestJson = LinkJson(server.Endpoint);

        using HttpResponseMessage failure = await host.PutAsync(requestJson, timeout.Token);

        await AssertLinkResponseAsync(failure, HttpStatusCode.ServiceUnavailable, WorkerId, "LinkFailed", timeout.Token);
        Assert.DoesNotContain(invalidHttpUri, await failure.Content.ReadAsStringAsync(timeout.Token), StringComparison.Ordinal);
        await worker.Disconnected.WaitAsync(timeout.Token);

        server.AddWorker(WorkerId, httpUri: "http://127.0.0.1:8080");
        using HttpResponseMessage retry = await host.PutAsync(requestJson, timeout.Token);

        await AssertLinkResponseAsync(retry, HttpStatusCode.OK, WorkerId, "Linked", timeout.Token);
        Assert.Equal(2, server.StreamCount);
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
        await AssertLinkResponseAsync(pendingConflict, HttpStatusCode.Conflict, WorkerId, "LinkFailed", timeout.Token);
        Assert.False(original.Response.IsCompleted);
        Assert.Equal(1, server.StreamCount);
        Assert.Equal(0, otherEndpoint.StreamCount);

        worker.CompleteInitialization();
        using HttpResponseMessage linked = await original.Response.WaitAsync(timeout.Token);
        await AssertLinkResponseAsync(linked, HttpStatusCode.OK, WorkerId, "Linked", timeout.Token);

        using HttpResponseMessage linkedConflict = await host.PutAsync(conflictJson, timeout.Token);
        await AssertLinkResponseAsync(linkedConflict, HttpStatusCode.Conflict, WorkerId, "LinkFailed", timeout.Token);
        Assert.Equal(1, server.StreamCount);
        Assert.Equal(0, otherEndpoint.StreamCount);
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

        await AssertLinkResponseAsync(retry, HttpStatusCode.OK, WorkerId, "Linked", timeout.Token);
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
        await AssertLinkResponseAsync(response, HttpStatusCode.OK, WorkerId, "Linked", timeout.Token);
        Assert.Equal(1, server.StreamCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{"workerPodName":"worker","workerGrpcEndpoint":"{endpoint}","broken":}""")]
    [InlineData("""{"workerGrpcEndpoint":"{endpoint}"}""")]
    [InlineData("""{"workerPodName":"worker"}""")]
    [InlineData("""{"workerPodName":"worker","workerGrpcEndpoint":"relative/path"}""")]
    [InlineData("""{"workerPodName":"worker","workerGrpcEndpoint":"ftp://127.0.0.1:5000"}""")]
    [InlineData("""{"workerPodName":"worker","workerGrpcEndpoint":"{endpoint}","workerHttpEndpoint":"relative/path"}""")]
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
    public async Task Put_EmptyObject_ReturnsBothRequiredFieldErrors()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);

        using HttpResponseMessage response = await host.PutAsync("{}", timeout.Token);
        JsonElement[] errors = await ReadValidationErrorsAsync(response, timeout.Token);

        Assert.Equal(
            [("workerPodName", "Required"), ("workerGrpcEndpoint", "Required")],
            errors.Select(error => (error.GetProperty("target").GetString(), error.GetProperty("code").GetString())));
        Assert.Equal(0, server.StreamCount);
    }

    [Fact]
    public async Task Put_MultipleInvalidFields_ReturnsAllErrors()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);
        const string json = """
            {"workerPodName":" ","workerGrpcEndpoint":"relative/grpc","workerHttpEndpoint":"relative/http"}
            """;

        using HttpResponseMessage response = await host.PutAsync(json, timeout.Token);
        JsonElement[] errors = await ReadValidationErrorsAsync(response, timeout.Token);

        Assert.Equal(
            [("workerPodName", "Required"), ("workerGrpcEndpoint", "InvalidEndpoint"), ("workerHttpEndpoint", "InvalidEndpoint")],
            errors.Select(error => (error.GetProperty("target").GetString(), error.GetProperty("code").GetString())));
        Assert.Equal(0, server.StreamCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"not an object\"")]
    [InlineData("""{"workerPodName":"worker","workerContainerEncryptionKey":{"private":"do-not-echo"}}""")]
    [InlineData("""{"workerPodName":"worker","workerGrpcEndpoint":""")]
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

    [Fact]
    public async Task Get_WorkersRoute_DoesNotMatch()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);

        using HttpResponseMessage response = await host.Client.GetAsync("/admin/workers", timeout.Token);

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

    private static string LinkJson(Uri endpoint, string workerId = WorkerId)
        => JsonSerializer.Serialize(new { workerPodName = workerId, workerGrpcEndpoint = endpoint.AbsoluteUri });

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
        HttpResponseMessage response, HttpStatusCode statusCode, string workerId, string status, CancellationToken cancellationToken)
    {
        Assert.Equal(statusCode, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        JsonElement body = document.RootElement;

        Assert.Equal(workerId, body.GetProperty("workerPodName").GetString());
        Assert.Equal(JsonValueKind.String, body.GetProperty("status").ValueKind);
        Assert.Equal(status, body.GetProperty("status").GetString());

        if (statusCode is HttpStatusCode.OK)
        {
            Assert.Equal(new[] { "status", "workerPodName" }, body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
            Assert.False(body.TryGetProperty("detail", out _));
        }
        else
        {
            Assert.Equal(new[] { "detail", "status", "workerPodName" }, body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
            Assert.Equal(JsonValueKind.String, body.GetProperty("detail").ValueKind);
            Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("detail").GetString()));
        }
    }
}
