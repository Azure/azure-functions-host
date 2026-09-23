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
/// Exercises the legacy MVC/Newtonsoft worker-link contract against the public registry interface.
/// Fixture authorization is permissive and does not test production authentication.
/// </summary>
public sealed class WorkerLinkEndpointIntegrationTests
{
    private const string WorkerId = "worker-pod-abc123";
    private static readonly Uri Endpoint = new("http://worker-proxy:50053");
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);
    private readonly Mock<IWorkerChannelRegistry> _registry = new(MockBehavior.Strict);

    [Theory]
    [InlineData(true, HttpStatusCode.Created)]
    [InlineData(false, HttpStatusCode.OK)]
    public async Task Put_ValidRequest_UsesRegistryCreationResult(bool isNewLink, HttpStatusCode status)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        SetupLink(isNewLink: isNewLink);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object);

        using HttpResponseMessage response = await host.PutAsync(LinkJson(), timeout.Token);

        await AssertLinkResponseAsync(response, status, timeout.Token);
        VerifyLink();
    }

    [Theory]
    [InlineData(true, HttpStatusCode.Created)]
    [InlineData(false, HttpStatusCode.OK)]
    public async Task Put_PendingRegistryResult_WaitsBeforeReturning(bool isNewLink, HttpStatusCode status)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        TaskCompletionSource invoked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<WorkerLinkResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _registry.Setup(value => value.LinkAsync(WorkerId, Endpoint, It.IsAny<CancellationToken>()))
            .Returns((string _, Uri _, CancellationToken token) =>
            {
                invoked.TrySetResult();
                return completion.Task.WaitAsync(token);
            });
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object);

        Task<HttpResponseMessage> request = host.PutAsync(LinkJson(), timeout.Token);
        await invoked.Task.WaitAsync(timeout.Token);
        Assert.False(request.IsCompleted);

        completion.SetResult(new WorkerLinkResult(null!, isNewLink));
        using HttpResponseMessage response = await request.WaitAsync(timeout.Token);
        await AssertLinkResponseAsync(response, status, timeout.Token);
        VerifyLink();
    }

    [Fact]
    public async Task Put_CanceledRequest_CancelsRegistryWait()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        TaskCompletionSource<CancellationToken> invoked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<WorkerLinkResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _registry.Setup(value => value.LinkAsync(WorkerId, Endpoint, It.IsAny<CancellationToken>()))
            .Returns((string _, Uri _, CancellationToken token) =>
            {
                invoked.TrySetResult(token);
                return completion.Task.WaitAsync(token);
            });
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object);

        Task<HttpResponseMessage> request = host.PutAsync(LinkJson(), cancellation.Token);
        CancellationToken registryToken = await invoked.Task.WaitAsync(timeout.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(timeout.Token));
        Assert.True(registryToken.IsCancellationRequested);
        VerifyLink();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    [InlineData("http://worker-proxy:28080")]
    [InlineData("https://worker-proxy:28080")]
    [InlineData("not-a-url")]
    [InlineData("relative/path")]
    public async Task Put_RemovedHttpEndpoint_IsIgnored(string? httpEndpoint)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        SetupLink();
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object);
        string json = JsonSerializer.Serialize(new { workerGrpcEndpoint = Endpoint.AbsoluteUri, workerHttpEndpoint = httpEndpoint });

        using HttpResponseMessage response = await host.PutAsync(json, timeout.Token);

        await AssertLinkResponseAsync(response, HttpStatusCode.Created, timeout.Token);
        VerifyLink();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{"workerGrpcEndpoint":"{endpoint}","broken":}""")]
    [InlineData("""{"workerHttpEndpoint":"{endpoint}"}""")]
    [InlineData("""{"workerGrpcEndpoint":"relative/path"}""")]
    [InlineData("""{"workerGrpcEndpoint":"ftp://127.0.0.1:5000"}""")]
    public async Task Put_InvalidRequest_ReturnsBadRequestWithoutCallingRegistry(string? json)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object);

        using HttpResponseMessage response = await host.PutAsync(
            json?.Replace("{endpoint}", Endpoint.AbsoluteUri, StringComparison.Ordinal), timeout.Token);

        await ReadValidationErrorsAsync(response, timeout.Token);
        _registry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Put_EmptyObject_ReturnsRequiredGrpcEndpointError()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object);

        using HttpResponseMessage response = await host.PutAsync("{}", timeout.Token);
        JsonElement error = Assert.Single(await ReadValidationErrorsAsync(response, timeout.Token));

        Assert.Equal("Required", error.GetProperty("code").GetString());
        Assert.Equal("workerGrpcEndpoint", error.GetProperty("target").GetString());
        _registry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Put_RemovedHttpEndpoint_DoesNotBypassGrpcEndpointValidation()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object);
        const string json = """
            {"workerGrpcEndpoint":"relative/grpc","workerHttpEndpoint":"relative/http"}
            """;

        using HttpResponseMessage response = await host.PutAsync(json, timeout.Token);
        JsonElement[] errors = await ReadValidationErrorsAsync(response, timeout.Token);

        Assert.Equal(
            [("workerGrpcEndpoint", "InvalidEndpoint")],
            errors.Select(error => (error.GetProperty("target").GetString(), error.GetProperty("code").GetString())));
        _registry.VerifyNoOtherCalls();
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
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object);

        using HttpResponseMessage response = await host.PutAsync(json, timeout.Token);
        JsonElement error = Assert.Single(await ReadValidationErrorsAsync(response, timeout.Token));

        Assert.Equal("InvalidBody", error.GetProperty("code").GetString());
        Assert.Equal("request", error.GetProperty("target").GetString());
        Assert.DoesNotContain("do-not-echo", await response.Content.ReadAsStringAsync(timeout.Token), StringComparison.Ordinal);
        _registry.VerifyNoOtherCalls();
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
        SetupLink();
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object);
        string json = $$"""
            {"workerGrpcEndpoint":"{{Endpoint.AbsoluteUri}}","{{propertyName}}":{{identityJson}}}
            """;

        using HttpResponseMessage response = await host.PutAsync(json, timeout.Token);

        await AssertLinkResponseAsync(response, HttpStatusCode.Created, timeout.Token);
        VerifyLink();
    }

    [Fact]
    public async Task Put_BodyWorkerPodName_DoesNotBypassEndpointConflict()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        _registry.Setup(value => value.LinkAsync(WorkerId, Endpoint, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WorkerLinkException(WorkerLinkFailureReason.Conflict, "Endpoint conflict."));
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object);
        string json = JsonSerializer.Serialize(new
        {
            workerPodName = "another-worker",
            workerGrpcEndpoint = Endpoint.AbsoluteUri,
        });

        using HttpResponseMessage response = await host.PutAsync(json, timeout.Token);

        await AssertLinkErrorAsync(response, HttpStatusCode.Conflict, "LinkConflict", timeout.Token);
        VerifyLink();
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    [InlineData("\u00a0\u2003")]
    public async Task Put_BlankWorkerIdentity_ReturnsRequiredWithoutCallingRegistry(string workerPodName)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object);

        using HttpResponseMessage response = await host.PutAsync(LinkJson(), timeout.Token, Uri.EscapeDataString(workerPodName));
        JsonElement error = Assert.Single(await ReadValidationErrorsAsync(response, timeout.Token));

        Assert.Equal("Required", error.GetProperty("code").GetString());
        Assert.Equal("workerPodName", error.GetProperty("target").GetString());
        _registry.VerifyNoOtherCalls();
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
        SetupLink(workerPodName);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object);

        using HttpResponseMessage response = await host.PutAsync(LinkJson(), timeout.Token, workerPodName);

        await AssertLinkResponseAsync(response, HttpStatusCode.Created, timeout.Token);
        VerifyLink(workerPodName);
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
    public async Task Put_OpaqueWorkerIdentity_PreservesRouteIdentity(string workerPodName)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        SetupLink(workerPodName);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object);

        using HttpResponseMessage response = await host.PutAsync(LinkJson(), timeout.Token, Uri.EscapeDataString(workerPodName));

        await AssertLinkResponseAsync(response, HttpStatusCode.Created, timeout.Token);
        VerifyLink(workerPodName);
    }

    [Theory]
    [InlineData("/admin/workers")]
    [InlineData("/admin/workers/worker-pod-abc123/extra")]
    public async Task Put_MissingOrExtraIdentityPathSegment_DoesNotMatch(string path)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object);

        using HttpResponseMessage response = await HttpClientJsonExtensions.PutAsJsonAsync(host.Client, path, new
        {
            workerPodName = WorkerId,
            workerGrpcEndpoint = Endpoint.AbsoluteUri,
        }, timeout.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        _registry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Get_WorkersRoute_DoesNotMatch()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object);

        using HttpResponseMessage response = await host.Client.GetAsync($"/admin/workers/{WorkerId}", timeout.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        _registry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Put_WithoutComputeComposition_DoesNotExposeWorkerRoute()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object, includeCompute: false);

        using HttpResponseMessage response = await host.PutAsync(LinkJson(), timeout.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        _registry.VerifyNoOtherCalls();
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
        _registry.Setup(value => value.LinkAsync(WorkerId, Endpoint, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WorkerLinkException(reason, privateDiagnostic, new Exception(privateDiagnostic)));
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object);

        using HttpResponseMessage response = await host.PutAsync(LinkJson(), timeout.Token);

        await AssertLinkErrorAsync(response, status, code, timeout.Token);
        Assert.DoesNotContain(privateDiagnostic, await response.Content.ReadAsStringAsync(timeout.Token), StringComparison.Ordinal);
        VerifyLink();
    }

    [Fact]
    public async Task Put_RegistryTimeout_ReturnsLinkTimeoutWithoutCancelingRequest()
    {
        const string privateDiagnostic = "private-timeout-detail-do-not-return";
        using var timeout = new CancellationTokenSource(TestTimeout);
        CancellationToken registryToken = default;
        _registry.Setup(value => value.LinkAsync(WorkerId, Endpoint, It.IsAny<CancellationToken>()))
            .Callback<string, Uri, CancellationToken>((_, _, token) => registryToken = token)
            .ThrowsAsync(new TimeoutException(privateDiagnostic));
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object);

        using HttpResponseMessage response = await host.PutAsync(LinkJson(), timeout.Token);

        await AssertLinkErrorAsync(response, HttpStatusCode.ServiceUnavailable, "LinkTimeout", timeout.Token);
        Assert.DoesNotContain(privateDiagnostic, await response.Content.ReadAsStringAsync(timeout.Token), StringComparison.Ordinal);
        Assert.False(registryToken.IsCancellationRequested);
        VerifyLink();
    }

    private static string LinkJson()
        => JsonSerializer.Serialize(new { workerGrpcEndpoint = Endpoint.AbsoluteUri });

    private void SetupLink(string workerId = WorkerId, bool isNewLink = true)
        => _registry.Setup(value => value.LinkAsync(workerId, Endpoint, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkerLinkResult(null!, isNewLink));

    private void VerifyLink(string workerId = WorkerId)
    {
        _registry.Verify(value => value.LinkAsync(workerId, Endpoint, It.IsAny<CancellationToken>()), Times.Once);
        _registry.VerifyNoOtherCalls();
    }

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
            Assert.Contains(error.GetProperty("target").GetString(), new[] { "request", "workerPodName", "workerGrpcEndpoint" });
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
