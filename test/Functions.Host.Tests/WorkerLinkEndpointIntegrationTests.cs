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
    public async Task Put_ValidRequest_WaitsForInitializationAndExactRetryReusesStream()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using WorkerLinkTestRpcServer worker = await WorkerLinkTestRpcServer.StartAsync(timeout.Token);
        WorkerLinkTestRpcServer.Handshake handshake = worker.Enqueue(WorkerId);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);

        WorkerLinkTestHost.PendingRequest request = host.Put(LinkJson(worker.Endpoint), timeout.Token);
        await request.WaitForProgressAsync(handshake.InitReceived, timeout.Token);

        Assert.False(request.Response.IsCompleted);
        Assert.Equal(1, worker.StreamCount);

        handshake.Succeed();
        using HttpResponseMessage response = await request.Response.WaitAsync(timeout.Token);
        await AssertLinkResponseAsync(response, HttpStatusCode.OK, WorkerId, "Linked", timeout.Token);

        using HttpResponseMessage retry = await host.Put(LinkJson(worker.Endpoint), timeout.Token).Response.WaitAsync(timeout.Token);
        await AssertLinkResponseAsync(retry, HttpStatusCode.OK, WorkerId, "Linked", timeout.Token);
        Assert.Equal(1, worker.StreamCount);
    }

    [Fact]
    public async Task Put_ConcurrentExactRetry_SharesPendingHandshake()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using WorkerLinkTestRpcServer worker = await WorkerLinkTestRpcServer.StartAsync(timeout.Token);
        WorkerLinkTestRpcServer.Handshake handshake = worker.Enqueue(WorkerId);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);

        WorkerLinkTestHost.PendingRequest original = host.Put(LinkJson(worker.Endpoint), timeout.Token);
        await original.WaitForProgressAsync(handshake.InitReceived, timeout.Token);
        WorkerLinkTestHost.PendingRequest retry = host.Put(LinkJson(worker.Endpoint), timeout.Token);
        await retry.WaitForProgressAsync(retry.ActionInvoked, timeout.Token);

        Assert.False(original.Response.IsCompleted);
        Assert.False(retry.Response.IsCompleted);
        Assert.Equal(1, worker.StreamCount);

        handshake.Succeed();
        using HttpResponseMessage originalResponse = await original.Response.WaitAsync(timeout.Token);
        using HttpResponseMessage retryResponse = await retry.Response.WaitAsync(timeout.Token);
        await AssertLinkResponseAsync(originalResponse, HttpStatusCode.OK, WorkerId, "Linked", timeout.Token);
        await AssertLinkResponseAsync(retryResponse, HttpStatusCode.OK, WorkerId, "Linked", timeout.Token);
        Assert.Equal(1, worker.StreamCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Put_DifferentWorkers_LinkIndependentlyAndReplayTheirOwnStreams(bool shareEndpoint)
    {
        const string secondWorkerId = "worker-pod-def456";
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using WorkerLinkTestRpcServer firstWorker = await WorkerLinkTestRpcServer.StartAsync(timeout.Token);
        await using WorkerLinkTestRpcServer otherEndpoint = await WorkerLinkTestRpcServer.StartAsync(timeout.Token);
        WorkerLinkTestRpcServer secondWorker = shareEndpoint ? firstWorker : otherEndpoint;
        WorkerLinkTestRpcServer.Handshake firstHandshake = firstWorker.Enqueue(WorkerId);
        WorkerLinkTestRpcServer.Handshake secondHandshake = secondWorker.Enqueue(secondWorkerId);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);

        WorkerLinkTestHost.PendingRequest first = host.Put(LinkJson(firstWorker.Endpoint), timeout.Token);
        await first.WaitForProgressAsync(firstHandshake.InitReceived, timeout.Token);
        WorkerLinkTestHost.PendingRequest second = host.Put(LinkJson(secondWorker.Endpoint, secondWorkerId), timeout.Token);
        await second.WaitForProgressAsync(secondHandshake.InitReceived, timeout.Token);

        Assert.False(first.Response.IsCompleted);
        Assert.False(second.Response.IsCompleted);
        Assert.Equal(2, firstWorker.StreamCount + otherEndpoint.StreamCount);

        secondHandshake.Succeed();
        using HttpResponseMessage secondResponse = await second.Response.WaitAsync(timeout.Token);
        await AssertLinkResponseAsync(secondResponse, HttpStatusCode.OK, secondWorkerId, "Linked", timeout.Token);
        Assert.False(first.Response.IsCompleted);

        firstHandshake.Succeed();
        using HttpResponseMessage firstResponse = await first.Response.WaitAsync(timeout.Token);
        await AssertLinkResponseAsync(firstResponse, HttpStatusCode.OK, WorkerId, "Linked", timeout.Token);
        using HttpResponseMessage firstReplay = await host.Put(LinkJson(firstWorker.Endpoint), timeout.Token).Response.WaitAsync(timeout.Token);
        using HttpResponseMessage secondReplay = await host.Put(
            LinkJson(secondWorker.Endpoint, secondWorkerId), timeout.Token).Response.WaitAsync(timeout.Token);
        await AssertLinkResponseAsync(firstReplay, HttpStatusCode.OK, WorkerId, "Linked", timeout.Token);
        await AssertLinkResponseAsync(secondReplay, HttpStatusCode.OK, secondWorkerId, "Linked", timeout.Token);
        Assert.Equal(shareEndpoint ? 2 : 1, firstWorker.StreamCount);
        Assert.Equal(shareEndpoint ? 0 : 1, otherEndpoint.StreamCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Put_RejectedInitialization_FailsWaitersAndAllowsRetry(bool concurrentRetry)
    {
        const string privateWorkerError = "private-worker-path-and-secret-do-not-return";
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using WorkerLinkTestRpcServer worker = await WorkerLinkTestRpcServer.StartAsync(timeout.Token);
        WorkerLinkTestRpcServer.Handshake rejected = worker.Enqueue(WorkerId);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);

        WorkerLinkTestHost.PendingRequest request = host.Put(LinkJson(worker.Endpoint), timeout.Token);
        await request.WaitForProgressAsync(rejected.InitReceived, timeout.Token);
        WorkerLinkTestHost.PendingRequest? duplicate = null;
        if (concurrentRetry)
        {
            duplicate = host.Put(LinkJson(worker.Endpoint), timeout.Token);
            await duplicate.WaitForProgressAsync(duplicate.ActionInvoked, timeout.Token);
            Assert.False(duplicate.Response.IsCompleted);
        }

        Assert.Equal(1, worker.StreamCount);
        rejected.Fail(privateWorkerError);

        using HttpResponseMessage failure = await request.Response.WaitAsync(timeout.Token);
        await AssertLinkResponseAsync(failure, HttpStatusCode.ServiceUnavailable, WorkerId, "LinkFailed", timeout.Token);
        Assert.DoesNotContain(privateWorkerError, await failure.Content.ReadAsStringAsync(timeout.Token), StringComparison.Ordinal);
        if (duplicate is not null)
        {
            using HttpResponseMessage duplicateFailure = await duplicate.Response.WaitAsync(timeout.Token);
            await AssertLinkResponseAsync(duplicateFailure, HttpStatusCode.ServiceUnavailable, WorkerId, "LinkFailed", timeout.Token);
            Assert.DoesNotContain(privateWorkerError, await duplicateFailure.Content.ReadAsStringAsync(timeout.Token), StringComparison.Ordinal);
        }

        await rejected.Disconnected.WaitAsync(timeout.Token);

        WorkerLinkTestRpcServer.Handshake accepted = worker.Enqueue(WorkerId);
        accepted.Succeed();
        using HttpResponseMessage retry = await host.Put(LinkJson(worker.Endpoint), timeout.Token).Response.WaitAsync(timeout.Token);

        await AssertLinkResponseAsync(retry, HttpStatusCode.OK, WorkerId, "Linked", timeout.Token);
        Assert.True(accepted.InitReceived.IsCompletedSuccessfully);
        Assert.Equal(2, worker.StreamCount);
    }

    [Fact]
    public async Task Put_SameWorkerWithConflictingEndpoint_ReturnsConflictWithoutDialing()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using WorkerLinkTestRpcServer worker = await WorkerLinkTestRpcServer.StartAsync(timeout.Token);
        await using WorkerLinkTestRpcServer otherEndpoint = await WorkerLinkTestRpcServer.StartAsync(timeout.Token);
        WorkerLinkTestRpcServer.Handshake handshake = worker.Enqueue(WorkerId);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);

        WorkerLinkTestHost.PendingRequest original = host.Put(LinkJson(worker.Endpoint), timeout.Token);
        await original.WaitForProgressAsync(handshake.InitReceived, timeout.Token);
        string conflictJson = LinkJson(otherEndpoint.Endpoint);

        using HttpResponseMessage pendingConflict = await host.Put(conflictJson, timeout.Token).Response.WaitAsync(timeout.Token);
        await AssertLinkResponseAsync(pendingConflict, HttpStatusCode.Conflict, WorkerId, "LinkFailed", timeout.Token);
        Assert.False(original.Response.IsCompleted);
        Assert.Equal(1, worker.StreamCount);
        Assert.Equal(0, otherEndpoint.StreamCount);

        handshake.Succeed();
        using HttpResponseMessage linked = await original.Response.WaitAsync(timeout.Token);
        await AssertLinkResponseAsync(linked, HttpStatusCode.OK, WorkerId, "Linked", timeout.Token);

        using HttpResponseMessage linkedConflict = await host.Put(conflictJson, timeout.Token).Response.WaitAsync(timeout.Token);
        await AssertLinkResponseAsync(linkedConflict, HttpStatusCode.Conflict, WorkerId, "LinkFailed", timeout.Token);
        Assert.Equal(1, worker.StreamCount);
        Assert.Equal(0, otherEndpoint.StreamCount);
    }

    [Fact]
    public async Task Put_CanceledHandshake_CleansAttemptAndAllowsRetry()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        await using WorkerLinkTestRpcServer worker = await WorkerLinkTestRpcServer.StartAsync(timeout.Token);
        WorkerLinkTestRpcServer.Handshake canceled = worker.Enqueue(WorkerId);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);

        WorkerLinkTestHost.PendingRequest request = host.Put(LinkJson(worker.Endpoint), cancellation.Token);
        await request.WaitForProgressAsync(canceled.InitReceived, timeout.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.Response.WaitAsync(timeout.Token));
        await request.ActionCompleted.WaitAsync(timeout.Token);
        await canceled.Disconnected.WaitAsync(timeout.Token);

        WorkerLinkTestRpcServer.Handshake accepted = worker.Enqueue(WorkerId);
        accepted.Succeed();
        using HttpResponseMessage retry = await host.Put(LinkJson(worker.Endpoint), timeout.Token).Response.WaitAsync(timeout.Token);

        await AssertLinkResponseAsync(retry, HttpStatusCode.OK, WorkerId, "Linked", timeout.Token);
        Assert.Equal(2, worker.StreamCount);
    }

    [Fact]
    public async Task Put_CanceledExactRetry_DoesNotCancelOriginalHandshake()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        await using WorkerLinkTestRpcServer worker = await WorkerLinkTestRpcServer.StartAsync(timeout.Token);
        WorkerLinkTestRpcServer.Handshake handshake = worker.Enqueue(WorkerId);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);

        WorkerLinkTestHost.PendingRequest original = host.Put(LinkJson(worker.Endpoint), timeout.Token);
        await original.WaitForProgressAsync(handshake.InitReceived, timeout.Token);
        WorkerLinkTestHost.PendingRequest retry = host.Put(LinkJson(worker.Endpoint), cancellation.Token);
        await retry.WaitForProgressAsync(retry.ActionInvoked, timeout.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retry.Response.WaitAsync(timeout.Token));
        await retry.ActionCompleted.WaitAsync(timeout.Token);
        Assert.False(original.Response.IsCompleted);
        Assert.False(handshake.Disconnected.IsCompleted);

        handshake.Succeed();
        using HttpResponseMessage response = await original.Response.WaitAsync(timeout.Token);
        await AssertLinkResponseAsync(response, HttpStatusCode.OK, WorkerId, "Linked", timeout.Token);
        Assert.Equal(1, worker.StreamCount);
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
        await using WorkerLinkTestRpcServer worker = await WorkerLinkTestRpcServer.StartAsync(timeout.Token);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);

        using HttpResponseMessage response = await host.Put(
            json?.Replace("{endpoint}", worker.Endpoint.AbsoluteUri, StringComparison.Ordinal),
            timeout.Token).Response.WaitAsync(timeout.Token);

        await ReadValidationErrorsAsync(response, timeout.Token);
        Assert.Equal(0, worker.StreamCount);
    }

    [Fact]
    public async Task Put_EmptyObject_ReturnsBothRequiredFieldErrors()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using WorkerLinkTestRpcServer worker = await WorkerLinkTestRpcServer.StartAsync(timeout.Token);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);

        using HttpResponseMessage response = await host.Put("{}", timeout.Token).Response.WaitAsync(timeout.Token);
        JsonElement[] errors = await ReadValidationErrorsAsync(response, timeout.Token);

        Assert.Equal(
            [("workerPodName", "Required"), ("workerGrpcEndpoint", "Required")],
            errors.Select(error => (error.GetProperty("target").GetString(), error.GetProperty("code").GetString())));
        Assert.Equal(0, worker.StreamCount);
    }

    [Fact]
    public async Task Put_MultipleInvalidFields_ReturnsAllErrors()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using WorkerLinkTestRpcServer worker = await WorkerLinkTestRpcServer.StartAsync(timeout.Token);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);
        const string json = """
            {"workerPodName":" ","workerGrpcEndpoint":"relative/grpc","workerHttpEndpoint":"relative/http"}
            """;

        using HttpResponseMessage response = await host.Put(json, timeout.Token).Response.WaitAsync(timeout.Token);
        JsonElement[] errors = await ReadValidationErrorsAsync(response, timeout.Token);

        Assert.Equal(
            [("workerPodName", "Required"), ("workerGrpcEndpoint", "InvalidEndpoint"), ("workerHttpEndpoint", "InvalidEndpoint")],
            errors.Select(error => (error.GetProperty("target").GetString(), error.GetProperty("code").GetString())));
        Assert.Equal(0, worker.StreamCount);
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
        await using WorkerLinkTestRpcServer worker = await WorkerLinkTestRpcServer.StartAsync(timeout.Token);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token);

        using HttpResponseMessage response = await host.Put(json, timeout.Token).Response.WaitAsync(timeout.Token);
        JsonElement error = Assert.Single(await ReadValidationErrorsAsync(response, timeout.Token));

        Assert.Equal("InvalidBody", error.GetProperty("code").GetString());
        Assert.Equal("request", error.GetProperty("target").GetString());
        Assert.DoesNotContain("do-not-echo", await response.Content.ReadAsStringAsync(timeout.Token), StringComparison.Ordinal);
        Assert.Equal(0, worker.StreamCount);
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
        await using WorkerLinkTestRpcServer worker = await WorkerLinkTestRpcServer.StartAsync(timeout.Token);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, includeCompute: false);

        using HttpResponseMessage response = await host.Put(LinkJson(worker.Endpoint), timeout.Token).Response.WaitAsync(timeout.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, worker.StreamCount);
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
