// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Host.WorkerLink;
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
    private static readonly Uri Endpoint = new("http://100.64.1.12:50053");
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);
    private readonly Mock<IWorkerChannelRegistry> _registry = new(MockBehavior.Strict);
    private readonly Mock<IWorkerLinkAcknowledgementClient> _acknowledgementClient = new(MockBehavior.Strict);

    public WorkerLinkEndpointIntegrationTests()
    {
        _acknowledgementClient
            .Setup(client => client.AcknowledgeAsync(
                It.IsAny<Uri>(),
                It.IsAny<FunctionsSplitPodIdentity>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Theory]
    [InlineData(true, HttpStatusCode.Created)]
    [InlineData(false, HttpStatusCode.OK)]
    public async Task Put_ValidRequest_UsesRegistryCreationResult(bool isNewLink, HttpStatusCode status)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        SetupLink(isNewLink: isNewLink);
        await using WorkerLinkTestHost host = await StartHostAsync(timeout.Token);

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
        await using WorkerLinkTestHost host = await StartHostAsync(timeout.Token);

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
        await using WorkerLinkTestHost host = await StartHostAsync(timeout.Token);

        Task<HttpResponseMessage> request = host.PutAsync(LinkJson(), cancellation.Token);
        CancellationToken registryToken = await invoked.Task.WaitAsync(timeout.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(timeout.Token));
        Assert.True(registryToken.IsCancellationRequested);
        VerifyLink(acknowledged: false);
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
        await using WorkerLinkTestHost host = await StartHostAsync(timeout.Token);
        Dictionary<string, object?> payload = CreateLinkPayload();
        payload["workerHttpEndpoint"] = httpEndpoint;
        string json = JsonSerializer.Serialize(payload);

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
        await using WorkerLinkTestHost host = await StartHostAsync(timeout.Token);

        using HttpResponseMessage response = await host.PutAsync(
            json?.Replace("{endpoint}", Endpoint.AbsoluteUri, StringComparison.Ordinal), timeout.Token);

        await ReadValidationErrorsAsync(response, timeout.Token);
        _registry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Put_EmptyObject_ReturnsRequiredGrpcEndpointError()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using WorkerLinkTestHost host = await StartHostAsync(timeout.Token);

        Dictionary<string, object?> payload = CreateLinkPayload();
        Assert.True(payload.Remove("workerGrpcEndpoint"));
        using HttpResponseMessage response = await host.PutAsync(JsonSerializer.Serialize(payload), timeout.Token);
        JsonElement error = Assert.Single(await ReadValidationErrorsAsync(response, timeout.Token));

        Assert.Equal("Required", error.GetProperty("code").GetString());
        Assert.Equal("workerGrpcEndpoint", error.GetProperty("target").GetString());
        _registry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Put_RemovedHttpEndpoint_DoesNotBypassGrpcEndpointValidation()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using WorkerLinkTestHost host = await StartHostAsync(timeout.Token);
        Dictionary<string, object?> payload = CreateLinkPayload();
        payload["workerGrpcEndpoint"] = "relative/grpc";
        payload["workerHttpEndpoint"] = "relative/http";

        using HttpResponseMessage response = await host.PutAsync(JsonSerializer.Serialize(payload), timeout.Token);
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
    [InlineData("""{"runtimeAccessKey":{"private":"do-not-echo"}}""")]
    [InlineData("""{"workerGrpcEndpoint":""")]
    public async Task Put_UnbindableBody_ReturnsSafeRequestError(string? json)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using WorkerLinkTestHost host = await StartHostAsync(timeout.Token);

        using HttpResponseMessage response = await host.PutAsync(json, timeout.Token);
        JsonElement error = Assert.Single(await ReadValidationErrorsAsync(response, timeout.Token));

        Assert.Equal("InvalidBody", error.GetProperty("code").GetString());
        Assert.Equal("request", error.GetProperty("target").GetString());
        Assert.DoesNotContain("do-not-echo", await response.Content.ReadAsStringAsync(timeout.Token), StringComparison.Ordinal);
        _registry.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("workerId", "\"worker-pod-abc123\"")]
    [InlineData("workerId", "\"another-worker\"")]
    [InlineData("workerId", "\"\"")]
    [InlineData("workerId", "null")]
    [InlineData("workerId", "123")]
    [InlineData("workerId", "true")]
    [InlineData("workerId", "{}")]
    [InlineData("workerId", "[]")]
    [InlineData("WorkerId", "null")]
    [InlineData("WORKERID", "\"another-worker\"")]
    [InlineData("workerPodName", "\"another-worker\"")]
    [InlineData("WorkerPodName", "null")]
    public async Task Put_BodyWorkerIdentity_IsIgnoredAndUsesRouteIdentity(string propertyName, string identityJson)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        SetupLink();
        await using WorkerLinkTestHost host = await StartHostAsync(timeout.Token);
        Dictionary<string, object?> payload = CreateLinkPayload();
        payload[propertyName] = JsonNode.Parse(identityJson);

        using HttpResponseMessage response = await host.PutAsync(JsonSerializer.Serialize(payload), timeout.Token);

        await AssertLinkResponseAsync(response, HttpStatusCode.Created, timeout.Token);
        VerifyLink();
    }

    [Fact]
    public async Task Put_BodyWorkerId_DoesNotBypassEndpointConflict()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        _registry.Setup(value => value.LinkAsync(WorkerId, Endpoint, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WorkerLinkException(WorkerLinkFailureReason.Conflict, "Endpoint conflict."));
        await using WorkerLinkTestHost host = await StartHostAsync(timeout.Token);
        Dictionary<string, object?> payload = CreateLinkPayload();
        payload["workerId"] = "another-worker";

        using HttpResponseMessage response = await host.PutAsync(JsonSerializer.Serialize(payload), timeout.Token);

        await AssertLinkErrorAsync(response, HttpStatusCode.Conflict, "LinkConflict", timeout.Token);
        VerifyLink(acknowledged: false);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    [InlineData("\u00a0\u2003")]
    public async Task Put_BlankWorkerIdentity_ReturnsRequiredWithoutCallingRegistry(string workerId)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using WorkerLinkTestHost host = await StartHostAsync(timeout.Token);

        using HttpResponseMessage response = await host.PutAsync(LinkJson(), timeout.Token, Uri.EscapeDataString(workerId));
        JsonElement error = Assert.Single(await ReadValidationErrorsAsync(response, timeout.Token));

        Assert.Equal("Required", error.GetProperty("code").GetString());
        Assert.Equal("workerId", error.GetProperty("target").GetString());
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
        string workerId = new('a', length);
        SetupLink(workerId);
        await using WorkerLinkTestHost host = await StartHostAsync(timeout.Token);

        using HttpResponseMessage response = await host.PutAsync(LinkJson(), timeout.Token, workerId);

        await AssertLinkResponseAsync(response, HttpStatusCode.Created, timeout.Token);
        VerifyLink(workerId);
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
    public async Task Put_OpaqueWorkerIdentity_PreservesRouteIdentity(string workerId)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        SetupLink(workerId);
        await using WorkerLinkTestHost host = await StartHostAsync(timeout.Token);

        using HttpResponseMessage response = await host.PutAsync(LinkJson(), timeout.Token, Uri.EscapeDataString(workerId));

        await AssertLinkResponseAsync(response, HttpStatusCode.Created, timeout.Token);
        VerifyLink(workerId);
    }

    [Theory]
    [InlineData("/admin/workers")]
    [InlineData("/admin/workers/worker-pod-abc123/extra")]
    public async Task Put_MissingOrExtraIdentityPathSegment_DoesNotMatch(string path)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using WorkerLinkTestHost host = await StartHostAsync(timeout.Token);

        using HttpResponseMessage response = await HttpClientJsonExtensions.PutAsJsonAsync(host.Client, path, new
            Dictionary<string, object?>(CreateLinkPayload()) { ["workerId"] = WorkerId },
            timeout.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        _registry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Get_WorkersRoute_DoesNotMatch()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using WorkerLinkTestHost host = await StartHostAsync(timeout.Token);

        using HttpResponseMessage response = await host.Client.GetAsync($"/admin/workers/{WorkerId}", timeout.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        _registry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Put_WithoutComputeComposition_DoesNotExposeWorkerRoute()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using WorkerLinkTestHost host = await StartHostAsync(timeout.Token, includeCompute: false);

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
        await using WorkerLinkTestHost host = await StartHostAsync(timeout.Token);

        using HttpResponseMessage response = await host.PutAsync(LinkJson(), timeout.Token);

        await AssertLinkErrorAsync(response, status, code, timeout.Token);
        Assert.DoesNotContain(privateDiagnostic, await response.Content.ReadAsStringAsync(timeout.Token), StringComparison.Ordinal);
        VerifyLink(acknowledged: false);
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
        await using WorkerLinkTestHost host = await StartHostAsync(timeout.Token);

        using HttpResponseMessage response = await host.PutAsync(LinkJson(), timeout.Token);

        await AssertLinkErrorAsync(response, HttpStatusCode.ServiceUnavailable, "LinkTimeout", timeout.Token);
        Assert.DoesNotContain(privateDiagnostic, await response.Content.ReadAsStringAsync(timeout.Token), StringComparison.Ordinal);
        Assert.False(registryToken.IsCancellationRequested);
        VerifyLink(acknowledged: false);
    }

    private Task<WorkerLinkTestHost> StartHostAsync(
        CancellationToken cancellationToken,
        bool includeCompute = true)
        => WorkerLinkTestHost.StartAsync(
            cancellationToken,
            _registry.Object,
            _acknowledgementClient.Object,
            includeCompute);

    private static string LinkJson() => JsonSerializer.Serialize(CreateLinkPayload());

    private static Dictionary<string, object?> CreateLinkPayload()
        => new()
        {
            ["workerGrpcEndpoint"] = Endpoint.Authority,
            ["runtimeAccessKey"] = "runtime-access-key",
            ["runtime"] = new
            {
                resourceNamespace = "runtime-namespace",
                podName = "runtime-pod",
                podGuid = "11111111-1111-1111-1111-111111111111",
                legionStampName = "legion-stamp",
                podType = "FunctionsSplitRuntimePod",
                app = new
                {
                    functionAppName = "test-app",
                    functionAppSiteId = 42,
                    antaresStampName = "antares-stamp"
                }
            }
        };

    private void SetupLink(string workerId = WorkerId, bool isNewLink = true)
        => _registry.Setup(value => value.LinkAsync(workerId, Endpoint, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkerLinkResult(null!, isNewLink));

    private void VerifyLink(string workerId = WorkerId, bool acknowledged = true)
    {
        _registry.Verify(value => value.LinkAsync(workerId, Endpoint, It.IsAny<CancellationToken>()), Times.Once);
        _registry.VerifyNoOtherCalls();
        if (acknowledged)
        {
            _acknowledgementClient.Verify(client => client.AcknowledgeAsync(
                Endpoint,
                It.Is<FunctionsSplitPodIdentity>(runtime =>
                    runtime.ResourceNamespace == "runtime-namespace"
                    && runtime.PodName == "runtime-pod"
                    && runtime.PodGuid == Guid.Parse("11111111-1111-1111-1111-111111111111")
                    && runtime.LegionStampName == "legion-stamp"
                    && runtime.PodType == "FunctionsSplitRuntimePod"
                    && runtime.App != null
                    && runtime.App.FunctionAppName == "test-app"
                    && runtime.App.FunctionAppSiteId == 42
                    && runtime.App.AntaresStampName == "antares-stamp"),
                It.IsAny<CancellationToken>()), Times.Once);
            _acknowledgementClient.VerifyNoOtherCalls();
        }
        else
        {
            _acknowledgementClient.VerifyNoOtherCalls();
        }
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
            Assert.Contains(
                error.GetProperty("code").GetString(),
                new[] { "Required", "InvalidEndpoint", "InvalidBody", "InvalidValue" });
            Assert.Contains(
                error.GetProperty("target").GetString(),
                new[]
                {
                    "request",
                    "workerId",
                    "workerGrpcEndpoint",
                    "runtimeAccessKey",
                    "runtime",
                    "runtime.resourceNamespace",
                    "runtime.podName",
                    "runtime.podGuid",
                    "runtime.legionStampName",
                    "runtime.podType",
                    "runtime.app",
                    "runtime.app.functionAppName",
                    "runtime.app.functionAppSiteId",
                    "runtime.app.antaresStampName"
                });
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
