// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Rpc.Client;
using Azure.Functions.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Azure.Functions.Host.Tests;

/// <summary>
/// Exercises the legacy MVC/Newtonsoft worker-link contract against the public registry interface.
/// The fixture uses the production container-management scheme and an independent synthetic platform token.
/// </summary>
public sealed class WorkerLinkEndpointIntegrationTests
{
    private const string WorkerId = "worker-pod-abc123";
    private static readonly Uri Endpoint = new("https://worker-proxy:50053");
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);
    private readonly Mock<IWorkerChannelRegistry> _registry = new(MockBehavior.Strict);

    [Theory]
    [InlineData("CONTAINER_ENCRYPTION_KEY", null, "MissingKey")]
    [InlineData("CONTAINER_ENCRYPTION_KEY", "private-invalid-key", "InvalidKey")]
    [InlineData("WEBSITE_POD_NAME", null, "MissingIdentity")]
    public async Task Startup_ValidatesManagementOptionsWithoutHostedServiceOrFatalConfigurationFailure(
        string setting, string? value, string reason)
    {
        using CancellationTokenSource timeout = new(TestTimeout);
        ConcurrentQueue<string> entries = new();
        Mock<ILogger> logger = new();
        logger.Setup(item => item.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        logger.Setup(item => item.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(new InvocationAction(call => entries.Enqueue(
                (string)((Delegate)call.Arguments[4]).DynamicInvoke(call.Arguments[2], call.Arguments[3])!)));
        Mock<ILoggerProvider> provider = new();
        provider.Setup(item => item.CreateLogger(It.IsAny<string>())).Returns(logger.Object);
        Dictionary<string, string?> values = new()
        {
            ["CONTAINER_ENCRYPTION_KEY"] = ManagementTokenTestData.RuntimeKey,
            ["WEBSITE_POD_NAME"] = ManagementTokenTestData.RuntimePodName,
            [setting] = value
        };
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object,
            authenticate: false, managementConfiguration: new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            loggerProvider: provider.Object);

        Assert.Contains(entries, message => message.Contains("configuration is unavailable", StringComparison.Ordinal)
            && message.Contains(reason, StringComparison.Ordinal));
        Assert.DoesNotContain(host.Services.GetServices<IHostedService>(), service =>
            service.GetType().FullName?.StartsWith("Azure.Functions.Authentication.", StringComparison.Ordinal) is true);
        Assert.DoesNotContain(entries, message => message.Contains("private-invalid-key", StringComparison.Ordinal)
            || message.Contains(ManagementTokenTestData.RuntimeKey, StringComparison.Ordinal));
        using HttpResponseMessage unrelated = await host.Client.GetAsync("/unrelated", timeout.Token);
        Assert.Equal(HttpStatusCode.NotFound, unrelated.StatusCode);
        Assert.DoesNotContain(entries, message => message.Contains("authentication failed:", StringComparison.Ordinal));

        using HttpResponseMessage protectedResponse = await host.PutAsync(LinkJson(), timeout.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, protectedResponse.StatusCode);
        Assert.Contains(entries, message => message.Contains("MissingToken", StringComparison.Ordinal));
        _registry.VerifyNoOtherCalls();
    }

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
        _registry.Setup(value => value.LinkAsync(WorkerId, Endpoint, ManagementTokenTestData.WorkerKey, It.IsAny<CancellationToken>()))
            .Returns((string _, Uri _, string _, CancellationToken token) =>
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
        _registry.Setup(value => value.LinkAsync(WorkerId, Endpoint, ManagementTokenTestData.WorkerKey, It.IsAny<CancellationToken>()))
            .Returns((string _, Uri _, string _, CancellationToken token) =>
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
    public async Task Put_UnknownEndpointField_IsIgnored(string? httpEndpoint)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        SetupLink();
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object);
        string json = JsonSerializer.Serialize(new
        {
            workerGrpcEndpoint = Endpoint.GetComponents(UriComponents.HostAndPort, UriFormat.UriEscaped),
            runtimeAccessKey = ManagementTokenTestData.WorkerKey,
            ignoredHttpEndpoint = httpEndpoint
        });

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
            json?.Replace("{endpoint}", Endpoint.GetComponents(UriComponents.HostAndPort, UriFormat.UriEscaped), StringComparison.Ordinal), timeout.Token);

        await ReadValidationErrorsAsync(response, timeout.Token);
        _registry.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("worker-proxy")]
    [InlineData("http://worker-proxy:50053")]
    public async Task Put_MissingPortOrPlaintextScheme_ReturnsEndpointErrorWithoutCallingRegistry(string endpoint)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object);
        string json = $$"""
            {"workerPodName":"worker-pod-abc123","workerGrpcEndpoint":"{{endpoint}}","runtimeAccessKey":"{{ManagementTokenTestData.WorkerKey}}"}
            """;

        using HttpResponseMessage response = await host.PutAsync(json, timeout.Token);

        JsonElement error = Assert.Single(await ReadValidationErrorsAsync(response, timeout.Token));
        Assert.Equal("InvalidEndpoint", error.GetProperty("code").GetString());
        Assert.Equal("workerGrpcEndpoint", error.GetProperty("target").GetString());
        _registry.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("missing", false)]
    [InlineData("worker-key", false)]
    [InlineData("app-key", false)]
    [InlineData("wrong-pod", false)]
    [InlineData("wrong-issuer", false)]
    [InlineData("expired", false)]
    [InlineData("future", false)]
    [InlineData("duplicate", false)]
    public async Task Put_AfterSpecialization_RequiresRuntimeOwnContainerToken(string scenario, bool accepted)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        if (accepted)
        {
            SetupLink();
        }
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object, authenticate: false);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string token = scenario switch
        {
            "worker-key" => ManagementTokenTestData.Create(ManagementTokenTestData.WorkerKey, ManagementTokenTestData.RuntimePodName),
            "app-key" => ManagementTokenTestData.Create(ManagementTokenTestData.AppKey, ManagementTokenTestData.RuntimePodName),
            "wrong-pod" => ManagementTokenTestData.Create(ManagementTokenTestData.RuntimeKey, WorkerId),
            "wrong-issuer" => ManagementTokenTestData.Create(ManagementTokenTestData.RuntimeKey, ManagementTokenTestData.RuntimePodName,
                issuer: "https://appservice.core.azurewebsites.net"),
            "expired" => ManagementTokenTestData.Create(ManagementTokenTestData.RuntimeKey, ManagementTokenTestData.RuntimePodName,
                notBefore: now.AddMinutes(-20), expires: now.AddMinutes(-6)),
            "future" => ManagementTokenTestData.Create(ManagementTokenTestData.RuntimeKey, ManagementTokenTestData.RuntimePodName,
                notBefore: now.AddMinutes(6), expires: now.AddMinutes(10)),
            _ => ManagementTokenTestData.Create(ManagementTokenTestData.RuntimeKey, ManagementTokenTestData.RuntimePodName)
        };
        if (!string.Equals(scenario, "missing", StringComparison.Ordinal))
        {
            host.Client.DefaultRequestHeaders.Add(ManagementTokenTestData.HeaderName,
                string.Equals(scenario, "duplicate", StringComparison.Ordinal) ? [token, token] : [token]);
        }
        using HttpResponseMessage response = await host.PutAsync(LinkJson(), timeout.Token);

        Assert.Equal(accepted ? HttpStatusCode.Created : HttpStatusCode.Unauthorized, response.StatusCode);
        if (accepted)
        {
            VerifyLink();
        }
        else
        {
            _registry.VerifyNoOtherCalls();
        }
    }

    [Fact]
    public async Task Put_EmptyObject_ReturnsRequiredEndpointAndKeyErrors()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object);

        using HttpResponseMessage response = await host.PutAsync("{}", timeout.Token);
        JsonElement[] errors = await ReadValidationErrorsAsync(response, timeout.Token);
        Assert.Equal(["workerGrpcEndpoint", "runtimeAccessKey"],
            errors.Select(error => error.GetProperty("target").GetString()));
        Assert.All(errors, error => Assert.Equal("Required", error.GetProperty("code").GetString()));
        _registry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Put_InvalidHttpOrigin_DoesNotBypassGrpcEndpointValidation()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object);
        const string json = """
            {"workerGrpcEndpoint":"relative/grpc","workerHttpEndpoint":"relative/http"}
            """;

        using HttpResponseMessage response = await host.PutAsync(json, timeout.Token);
        JsonElement[] errors = await ReadValidationErrorsAsync(response, timeout.Token);

        Assert.Equal(
            [("workerGrpcEndpoint", "InvalidEndpoint"), ("runtimeAccessKey", "Required")],
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
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object);
        string json = $$"""
            {"workerGrpcEndpoint":"{{Endpoint.GetComponents(UriComponents.HostAndPort, UriFormat.UriEscaped)}}","runtimeAccessKey":"{{ManagementTokenTestData.WorkerKey}}","{{propertyName}}":{{identityJson}}}
            """;

        using HttpResponseMessage response = await host.PutAsync(json, timeout.Token);

        await AssertLinkResponseAsync(response, HttpStatusCode.Created, timeout.Token);
        VerifyLink();
    }

    [Fact]
    public async Task Put_BodyWorkerId_DoesNotBypassEndpointConflict()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        _registry.Setup(value => value.LinkAsync(WorkerId, Endpoint, ManagementTokenTestData.WorkerKey, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WorkerLinkException(WorkerLinkFailureReason.Conflict, "Endpoint conflict."));
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object);
        string json = JsonSerializer.Serialize(new
        {
            workerId = "another-worker",
            workerGrpcEndpoint = Endpoint.GetComponents(UriComponents.HostAndPort, UriFormat.UriEscaped),
            runtimeAccessKey = ManagementTokenTestData.WorkerKey,
        });

        using HttpResponseMessage response = await host.PutAsync(json, timeout.Token);

        await AssertLinkErrorAsync(response, HttpStatusCode.Conflict, "LinkConflict", timeout.Token);
        VerifyLink();
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    [InlineData("\u00a0\u2003")]
    public async Task Put_BlankWorkerIdentity_ReturnsRequiredWithoutCallingRegistry(string workerId)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object);

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
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object);

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
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object);

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
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object);

        using HttpResponseMessage response = await HttpClientJsonExtensions.PutAsJsonAsync(host.Client, path, new
        {
            workerId = WorkerId,
            workerGrpcEndpoint = Endpoint.GetComponents(UriComponents.HostAndPort, UriFormat.UriEscaped),
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
    [InlineData(WorkerLinkFailureReason.AuthenticationUnavailable, HttpStatusCode.ServiceUnavailable, "AuthenticationUnavailable")]
    public async Task Put_LinkFailure_ReturnsExactErrorEnvelope(WorkerLinkFailureReason reason, HttpStatusCode status, string code)
    {
        const string privateDiagnostic = "private-worker-detail-do-not-return";
        using var timeout = new CancellationTokenSource(TestTimeout);
        _registry.Setup(value => value.LinkAsync(WorkerId, Endpoint, ManagementTokenTestData.WorkerKey, It.IsAny<CancellationToken>()))
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
        _registry.Setup(value => value.LinkAsync(WorkerId, Endpoint, ManagementTokenTestData.WorkerKey, It.IsAny<CancellationToken>()))
            .Callback<string, Uri, string, CancellationToken>((_, _, _, token) => registryToken = token)
            .ThrowsAsync(new TimeoutException(privateDiagnostic));
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object);

        using HttpResponseMessage response = await host.PutAsync(LinkJson(), timeout.Token);

        await AssertLinkErrorAsync(response, HttpStatusCode.ServiceUnavailable, "LinkTimeout", timeout.Token);
        Assert.DoesNotContain(privateDiagnostic, await response.Content.ReadAsStringAsync(timeout.Token), StringComparison.Ordinal);
        Assert.False(registryToken.IsCancellationRequested);
        VerifyLink();
    }

    private static string LinkJson()
        => JsonSerializer.Serialize(new
        {
            workerGrpcEndpoint = Endpoint.GetComponents(UriComponents.HostAndPort, UriFormat.UriEscaped),
            runtimeAccessKey = ManagementTokenTestData.WorkerKey
        });

    [Fact]
    public async Task Put_RemovedHttpOrigin_IsIgnored()
    {
        using CancellationTokenSource timeout = new(TestTimeout);
        Uri httpEndpoint = new("https://worker-proxy:48801");
        _registry.Setup(value => value.LinkAsync(WorkerId, Endpoint, ManagementTokenTestData.WorkerKey,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkerLinkResult(null!, true));
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(timeout.Token, _registry.Object);
        string json = JsonSerializer.Serialize(new
        {
            workerGrpcEndpoint = Endpoint.GetComponents(UriComponents.HostAndPort, UriFormat.UriEscaped),
            workerHttpEndpoint = httpEndpoint.AbsoluteUri,
            runtimeAccessKey = ManagementTokenTestData.WorkerKey
        });

        using HttpResponseMessage response = await host.PutAsync(json, timeout.Token);

        await AssertLinkResponseAsync(response, HttpStatusCode.Created, timeout.Token);
        _registry.Verify(value => value.LinkAsync(WorkerId, Endpoint, ManagementTokenTestData.WorkerKey,
            It.IsAny<CancellationToken>()), Times.Once);
        _registry.VerifyNoOtherCalls();
    }

    private void SetupLink(string workerId = WorkerId, bool isNewLink = true)
        => _registry.Setup(value => value.LinkAsync(workerId, Endpoint, ManagementTokenTestData.WorkerKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkerLinkResult(null!, isNewLink));

    private void VerifyLink(string workerId = WorkerId)
    {
        _registry.Verify(value => value.LinkAsync(workerId, Endpoint, ManagementTokenTestData.WorkerKey, It.IsAny<CancellationToken>()), Times.Once);
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
            Assert.Contains(error.GetProperty("code").GetString(), new[] { "Required", "InvalidEndpoint", "InvalidBody", "InvalidValue" });
            Assert.Contains(error.GetProperty("target").GetString(), new[] { "request", "workerId", "workerGrpcEndpoint", "workerHttpEndpoint", "runtimeAccessKey" });
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
