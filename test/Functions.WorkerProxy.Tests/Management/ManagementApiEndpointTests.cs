// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Management;
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests.Management;

public class ManagementApiEndpointTests
{
    private const string AssignPath = "/admin/worker/assignment";
    private const string StatePath = "/admin/worker/state";
    private const string ValidAssignment = """
        {"startupMode":"SpecializationRequired","functionAppName":"test-app","functionGroupName":"test-group","isAlwaysReady":false,
         "environment":{"SETTING":"private-value"},"functionAppDirectory":"/home/site/wwwroot"}
        """;

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task OmittedRevisionReturnsRevisionZeroImmediately()
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpResponseMessage response = await client.GetAsync(StatePath, timeout.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoStore(response);
        using JsonDocument state = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        Assert.Equal(0, state.RootElement.GetProperty("revisionId").GetInt64());
        Assert.Equal("None", state.RootElement.GetProperty("workerPodState").GetProperty("podStatus").GetString());
        Assert.False(state.RootElement.GetProperty("workerPodState").TryGetProperty("startupMode", out _));
        Assert.Equal(0, factory.Services.GetRequiredService<WorkerPodStateManager>().PendingWaiterCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("true")]
    [InlineData("\"assignment\"")]
    public async Task InvalidBodyReturnsBadRequestWithoutChangingAssignment(string? body)
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        manager.OnWorkerAttached(1);
        manager.OnWorkerStartStream(1, "worker");
        WorkerPodState before = manager.State;
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        using StringContent? content = body is null ? null : JsonBody(body);
        using HttpResponseMessage response = await client.PutAsync(AssignPath, content, timeout.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Same(before, manager.State);
        Assert.Null(manager.State.StartupMode);

        using StringContent validContent = JsonBody(ValidAssignment);
        using HttpResponseMessage accepted = await client.PutAsync(AssignPath, validContent, timeout.Token);
        await AssertAssignmentSuccessAsync(accepted, HttpStatusCode.Created, timeout.Token);
        WorkerPodState assigned = manager.State;

        using StringContent? rejectedContent = body is null ? null : JsonBody(body);
        using HttpResponseMessage rejected = await client.PutAsync(AssignPath, rejectedContent, timeout.Token);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Same(assigned, manager.State);

        using StringContent replayContent = JsonBody(ValidAssignment);
        using HttpResponseMessage replay = await client.PutAsync(AssignPath, replayContent, timeout.Token);
        await AssertAssignmentSuccessAsync(replay, HttpStatusCode.NoContent, timeout.Token);
        Assert.Same(assigned, manager.State);
    }

    [Theory]
    [InlineData("startupMode", null, "Required")]
    [InlineData("startupMode", "null", "Required")]
    [InlineData("startupMode", "\"\"", "Required")]
    [InlineData("startupMode", "\" \\t\\r\\n\"", "Required")]
    [InlineData("startupMode", "\"preconfigured\"", "InvalidValue")]
    [InlineData("startupMode", "\"specializationRequired\"", "InvalidValue")]
    [InlineData("startupMode", "\"Preconfigured \"", "InvalidValue")]
    [InlineData("startupMode", "\" SpecializationRequired\"", "InvalidValue")]
    [InlineData("startupMode", "\"Unknown\"", "InvalidValue")]
    [InlineData("startupMode", "\"0\"", "InvalidValue")]
    [InlineData("startupMode", "0", null)]
    [InlineData("startupMode", "true", null)]
    [InlineData("startupMode", "{}", null)]
    [InlineData("startupMode", "[]", null)]
    [InlineData("functionAppName", null, "Required")]
    [InlineData("functionAppName", "\" \"", "Required")]
    [InlineData("functionGroupName", null, "Required")]
    [InlineData("functionGroupName", "\"\"", "Required")]
    [InlineData("functionAppDirectory", null, "Required")]
    [InlineData("functionAppDirectory", "null", "Required")]
    [InlineData("functionAppDirectory", "\"\"", "Required")]
    [InlineData("functionAppDirectory", "\" \"", "Required")]
    [InlineData("functionAppDirectory", null, "Required", "Preconfigured")]
    [InlineData("functionAppDirectory", "null", "Required", "Preconfigured")]
    [InlineData("isAlwaysReady", null, "Required")]
    [InlineData("isAlwaysReady", "null", "Required")]
    [InlineData("isAlwaysReady", "\"false\"", null)]
    [InlineData("environment", null, "Required")]
    [InlineData("environment", "null", "Required")]
    [InlineData("environment", "{\"\":\"private-value\"}", "InvalidValue")]
    [InlineData("environment", "{\"SETTING\":null}", "InvalidValue")]
    [InlineData("environment", "{\"SETTING\":123}", null)]
    [InlineData("environment", null, "Required", "Preconfigured")]
    [InlineData("environment", "null", "Required", "Preconfigured")]
    [InlineData("environment", "[]", null, "Preconfigured")]
    [InlineData("environment", "{\"\":\"private-value\"}", "InvalidValue", "Preconfigured")]
    [InlineData("environment", "{\"SETTING\":null}", "InvalidValue", "Preconfigured")]
    [InlineData("environment", "{\"SETTING\":123}", null, "Preconfigured")]
    public async Task InvalidAssignmentDoesNotClaimIdentity(
        string field, string? value, string? code, string startupMode = "SpecializationRequired")
    {
        JsonObject body = JsonNode.Parse(ValidAssignment)!.AsObject();
        body["startupMode"] = startupMode;
        if (value is null)
        {
            body.Remove(field);
        }
        else
        {
            body[field] = JsonNode.Parse(value);
        }

        await using WorkerProxyWebApplicationFactory factory = new();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        Assert.True(manager.OnWorkerAttached(1));
        Assert.True(manager.OnWorkerStartStream(1, "worker"));
        WorkerPodState before = manager.State;
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        using StringContent content = JsonBody(body.ToJsonString());
        using HttpResponseMessage response = await client.PutAsync(AssignPath, content, timeout.Token);

        if (code is null)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        else
        {
            await AssertValidationAsync(response, timeout.Token, (code, field));
        }
        WorkerPodState state = manager.State;
        Assert.Same(before, state);
        Assert.Equal(2, state.Revision);
        Assert.Equal(WorkerAssignmentState.Unassigned, state.AssignmentState);
        Assert.Null(state.FunctionAppName);
        Assert.Null(state.StartupMode);

        using StringContent validContent = JsonBody(
            ValidAssignment.Replace("test-app", "another-app", StringComparison.Ordinal));
        using HttpResponseMessage accepted = await client.PutAsync(AssignPath, validContent, timeout.Token);
        await AssertAssignmentSuccessAsync(accepted, HttpStatusCode.Created, timeout.Token);
        Assert.Equal("another-app", manager.State.FunctionAppName);

        WorkerPodState assigned = manager.State;
        using StringContent rejectedContent = JsonBody(body.ToJsonString());
        using HttpResponseMessage rejected = await client.PutAsync(AssignPath, rejectedContent, timeout.Token);
        if (code is null)
        {
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        else
        {
            await AssertValidationAsync(rejected, timeout.Token, (code, field));
        }

        Assert.Same(assigned, manager.State);
    }

    [Theory]
    [InlineData("""{"startupMode":"SpecializationRequired"}""")]
    [InlineData("""
        {"startupMode":"SpecializationRequired","functionAppName":" ","functionGroupName":"","isAlwaysReady":null,
         "functionAppDirectory":" ","environment":{"PRIVATE_SETTING":null}}
        """)]
    public async Task AssignmentReturnsAllInvalidFieldsInOneResponse(string body)
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        using StringContent content = JsonBody(body);
        using HttpResponseMessage response = await client.PutAsync(AssignPath, content, timeout.Token);

        string environmentError = string.Equals(body, """{"startupMode":"SpecializationRequired"}""", StringComparison.Ordinal)
            ? "Required" : "InvalidValue";
        await AssertValidationAsync(response, timeout.Token,
            ("Required", "functionAppName"),
            ("Required", "functionGroupName"),
            ("Required", "isAlwaysReady"),
            ("Required", "functionAppDirectory"),
            (environmentError, "environment"));
        string json = await response.Content.ReadAsStringAsync(timeout.Token);
        Assert.DoesNotContain("PRIVATE_SETTING", json);
        Assert.Equal(0, factory.Services.GetRequiredService<WorkerPodStateManager>().State.Revision);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("text/plain")]
    [InlineData("application/xml")]
    [InlineData("application/octet-stream")]
    [InlineData("text/json")]
    [InlineData("text/plain; charset=not-a-real-charset")]
    public async Task UnsupportedMediaTypeReturns415WithoutChangingAssignment(string? contentType)
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        manager.OnWorkerAttached(1);
        manager.OnWorkerStartStream(1, "worker");
        WorkerPodState before = manager.State;
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        using StringContent content = JsonBody(ValidAssignment);
        content.Headers.Remove("Content-Type");
        if (contentType is not null)
        {
            Assert.True(content.Headers.TryAddWithoutValidation("Content-Type", contentType));
        }

        using HttpResponseMessage response = await client.PutAsync(AssignPath, content, timeout.Token);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Same(before, manager.State);
        Assert.Null(manager.State.StartupMode);

        using StringContent validContent = JsonBody(ValidAssignment);
        using HttpResponseMessage accepted = await client.PutAsync(AssignPath, validContent, timeout.Token);
        await AssertAssignmentSuccessAsync(accepted, HttpStatusCode.Created, timeout.Token);
        WorkerPodState assigned = manager.State;

        JsonObject different = JsonNode.Parse(ValidAssignment)!.AsObject();
        different["startupMode"] = "Preconfigured";
        different["functionAppName"] = "other-app";
        using StringContent conflictingContent = JsonBody(different.ToJsonString());
        conflictingContent.Headers.Remove("Content-Type");
        if (contentType is not null)
        {
            Assert.True(conflictingContent.Headers.TryAddWithoutValidation("Content-Type", contentType));
        }

        using HttpResponseMessage rejected = await client.PutAsync(AssignPath, conflictingContent, timeout.Token);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, rejected.StatusCode);
        Assert.Same(assigned, manager.State);

        using StringContent replayContent = JsonBody(ValidAssignment);
        using HttpResponseMessage replay = await client.PutAsync(AssignPath, replayContent, timeout.Token);
        await AssertAssignmentSuccessAsync(replay, HttpStatusCode.NoContent, timeout.Token);
        Assert.Same(assigned, manager.State);
    }

    [Fact]
    public async Task AssignmentBindingUsesRegisteredSourceGeneratedJsonMetadata()
    {
        await using WorkerProxyWebApplicationFactory factory = new(configureServices: services =>
            services.PostConfigure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
            {
                Assert.Same(WorkerProxyJsonContext.Default, options.SerializerOptions.TypeInfoResolverChain[0]);
                // Retain only the registered context so this request cannot fall back to reflection.
                while (options.SerializerOptions.TypeInfoResolverChain.Count > 1)
                {
                    options.SerializerOptions.TypeInfoResolverChain.RemoveAt(1);
                }
            }));
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        manager.OnWorkerAttached(1);
        manager.OnWorkerStartStream(1, "worker");
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        using StringContent content = JsonBody(ValidAssignment.Replace(
            "startupMode", "STARTUPMODE", StringComparison.Ordinal));

        using HttpResponseMessage response = await client.PutAsync(AssignPath, content, timeout.Token);

        await AssertAssignmentSuccessAsync(response, HttpStatusCode.Created, timeout.Token);
        Assert.Equal(WorkerStartupMode.SpecializationRequired, manager.State.StartupMode);
        Assert.False(manager.State.IsAlwaysReady);
    }

    [Theory]
    [InlineData("application/json", "utf-8")]
    [InlineData("application/problem+json", "utf-8")]
    [InlineData("APPLICATION/JSON", "utf-8")]
    [InlineData("application/json; charset=utf-8", "utf-8")]
    [InlineData("application/json; charset=UTF-8", "utf-8")]
    [InlineData("application/json; charset=utf-16", "utf-16")]
    [InlineData("application/problem+json; charset=utf-16", "utf-16")]
    public async Task SupportedJsonCharsetPreservesAssignment(string contentType, string encodingName)
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        manager.OnWorkerAttached(1);
        manager.OnWorkerStartStream(1, "worker");
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        const string appName = "test-app-\u00e9";
        string body = ValidAssignment.Replace("test-app", appName, StringComparison.Ordinal);
        using StringContent content = new(body, Encoding.GetEncoding(encodingName));
        content.Headers.Remove("Content-Type");
        Assert.True(content.Headers.TryAddWithoutValidation("Content-Type", contentType));

        using HttpResponseMessage response = await client.PutAsync(AssignPath, content, timeout.Token);

        await AssertAssignmentSuccessAsync(response, HttpStatusCode.Created, timeout.Token);
        Assert.Equal(appName, manager.State.FunctionAppName);
    }

    [Theory]
    [InlineData("lastKnownRevision=-1")]
    [InlineData("lastKnownRevision=1")]
    [InlineData("lastKnownRevision=9223372036854775807")]
    [InlineData("lastKnownRevision=9223372036854775808")]
    [InlineData("lastKnownRevision=-9223372036854775809")]
    [InlineData("lastKnownRevision=-9223372036854775808")]
    [InlineData("lastKnownRevision=null")]
    [InlineData("lastKnownRevision=")]
    [InlineData("lastKnownRevision")]
    [InlineData("lastKnownRevision=0&lastKnownRevision=0")]
    [InlineData("lastKnownRevision=0&lastKnownRevision=1")]
    [InlineData("lastKnownRevision=0&lastKnownRevision=")]
    [InlineData("lastKnownRevision=0&LASTKNOWNREVISION=0")]
    [InlineData("lastKnownRevision=1.5")]
    [InlineData("lastKnownRevision=0.0")]
    [InlineData("lastKnownRevision=1e0")]
    [InlineData("lastKnownRevision=%220%22")]
    [InlineData("lastKnownRevision=true")]
    [InlineData("lastKnownRevision=%200")]
    [InlineData("lastKnownRevision=0%20")]
    [InlineData("lastKnownRevision=%090")]
    [InlineData("lastKnownRevision=0%0A")]
    [InlineData("lastKnownRevision=%000")]
    [InlineData("lastKnownRevision=0%000")]
    [InlineData("lastKnownRevision=%C2%A00")]
    [InlineData("lastKnownRevision=%2B%200")]
    [InlineData("lastKnownRevision=+0")]
    [InlineData("lastKnownRevision=%2B")]
    [InlineData("lastKnownRevision=-")]
    [InlineData("lastKnownRevision=%2B-0")]
    [InlineData("lastKnownRevision=0,0")]
    [InlineData("lastKnownRevision=%D9%A0")]
    public async Task InvalidRevisionUsesHostValidationEnvelope(string query)
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpResponseMessage response = await client.GetAsync($"{StatePath}?{query}", timeout.Token);

        await AssertValidationAsync(response, timeout.Token, ("InvalidRevision", "lastKnownRevision"));
        AssertNoStore(response);
        Assert.Equal(0, factory.Services.GetRequiredService<WorkerPodStateManager>().PendingWaiterCount);
        Assert.Equal(0, factory.Services.GetRequiredService<WorkerPodStateManager>().State.Revision);
    }

    [Theory]
    [InlineData("/admin/worker/ready", "GET")]
    [InlineData(AssignPath, "PUT")]
    [InlineData(StatePath, "GET")]
    public async Task WorkerManagementRoutesAreUnavailableOnOtherListeners(string path, string method)
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        using CancellationTokenSource timeout = new(TestTimeout);
        foreach (FunctionRpcRelaySide side in new[] { FunctionRpcRelaySide.Runtime, FunctionRpcRelaySide.Worker })
        {
            using HttpClient rpcClient = new() { BaseAddress = factory.GetFunctionRpcAddress(side) };
            using HttpRequestMessage request = new(new HttpMethod(method), path)
            {
                Version = HttpVersion.Version20,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact,
                Content = string.Equals(method, "PUT", StringComparison.Ordinal) ? JsonBody("{}") : null
            };
            using HttpResponseMessage response = await rpcClient.SendAsync(request, timeout.Token);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using HttpClient forwardingClient = factory.CreateHttpForwardingClient();
        using HttpRequestMessage forwardingRequest = new(new HttpMethod(method), path)
        {
            Content = string.Equals(method, "PUT", StringComparison.Ordinal) ? JsonBody("{}") : null
        };
        using HttpResponseMessage forwardingResponse = await forwardingClient.SendAsync(forwardingRequest, timeout.Token);
        Assert.Equal(HttpStatusCode.NotFound, forwardingResponse.StatusCode);
    }

    [Theory]
    [InlineData("/admin/worker/ready", "POST")]
    [InlineData("/admin/worker/ready", "PUT")]
    [InlineData(AssignPath, "GET")]
    [InlineData(AssignPath, "POST")]
    [InlineData(AssignPath, "PATCH")]
    [InlineData(AssignPath, "DELETE")]
    [InlineData(StatePath, "POST")]
    [InlineData(StatePath, "PUT")]
    [InlineData(StatePath, "DELETE")]
    public async Task ManagementRoutesRejectUnsupportedMethods(string path, string method)
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpRequestMessage request = new(new HttpMethod(method), path);
        using HttpResponseMessage response = await client.SendAsync(request, timeout.Token);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("%2B0")]
    [InlineData("-0")]
    [InlineData("000")]
    [InlineData("0%00")]
    public async Task ExplicitZeroRevisionPollReturnsNoContentAtDeadline(string revision)
    {
        Mock<TimeProvider> clock = new();
        Mock<ITimer> timer = new();
        TaskCompletionSource disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        timer.Setup(instance => instance.Dispose()).Callback(() => disposed.TrySetResult());
        TaskCompletionSource<Action> expire = new(TaskCreationOptions.RunContinuationsAsynchronously);
        clock.Setup(provider => provider.CreateTimer(
            It.IsAny<TimerCallback>(), It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
            .Returns((TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            {
                Assert.Equal(TimeSpan.FromSeconds(60), dueTime);
                Assert.Equal(Timeout.InfiniteTimeSpan, period);
                expire.TrySetResult(() => callback(state));
                return timer.Object;
            });
        await using WorkerProxyWebApplicationFactory factory = new(configureServices: services =>
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock.Object)));
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        Task<HttpResponseMessage> poll = client.GetAsync($"{StatePath}?lastKnownRevision={revision}", timeout.Token);
        Action fireTimer = await expire.Task.WaitAsync(timeout.Token);
        Assert.False(poll.IsCompleted);

        fireTimer();
        using HttpResponseMessage response = await poll;
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        AssertNoStore(response);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(timeout.Token));
        Assert.Equal(0, factory.Services.GetRequiredService<WorkerPodStateManager>().PendingWaiterCount);
        await disposed.Task.WaitAsync(timeout.Token);
        timer.Verify(instance => instance.Dispose(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ClientCancellationRemovesPendingPoll()
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        Task<HttpResponseMessage> poll = client.GetAsync($"{StatePath}?lastKnownRevision=0", cancellation.Token);
        while (manager.PendingWaiterCount == 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => poll);
        while (manager.PendingWaiterCount != 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }

        Assert.Equal(0, manager.State.Revision);
    }

    [Theory]
    [InlineData("/admin/worker/assign", "POST")]
    [InlineData("/admin/worker/assign", "PUT")]
    [InlineData("/admin/infra/instanceState", "POST")]
    [InlineData("/admin/infra/instanceState", "GET")]
    public async Task ObsoleteManagementRoutesAreNotFound(string path, string method)
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpRequestMessage request = new(new HttpMethod(method), path);
        using HttpResponseMessage response = await client.SendAsync(request, timeout.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("%2B0")]
    [InlineData("-0")]
    [InlineData("1")]
    [InlineData("%2B1")]
    [InlineData("0001")]
    [InlineData("1%00")]
    public async Task OlderSignedInvariantRevisionReturnsCurrentSnapshot(string revision)
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        Assert.True(manager.OnWorkerAttached(1));
        Assert.True(manager.OnWorkerStartStream(1, "worker"));
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpResponseMessage response = await client.GetAsync($"{StatePath}?lastKnownRevision={revision}", timeout.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoStore(response);
        using JsonDocument state = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        Assert.Equal(2, state.RootElement.GetProperty("revisionId").GetInt64());
        Assert.Equal(2, manager.State.Revision);
        Assert.Equal(0, manager.PendingWaiterCount);
    }

    [Theory]
    [InlineData(null, "{", "application/json")]
    [InlineData(null, "not-json", "text/plain")]
    [InlineData("0", "{\"lastKnownRevision\":999}", "application/json")]
    [InlineData("%2B1", "not-json", "application/octet-stream")]
    public async Task StateQueryIgnoresRequestBodyAndContentType(string? revision, string body, string contentType)
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        Assert.True(manager.OnWorkerAttached(1));
        Assert.True(manager.OnWorkerStartStream(1, "worker"));
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        string path = revision is null ? StatePath : $"{StatePath}?lastKnownRevision={revision}";
        using HttpRequestMessage request = new(HttpMethod.Get, path)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType)
        };
        using HttpResponseMessage response = await client.SendAsync(request, timeout.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoStore(response);
        using JsonDocument state = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        Assert.Equal(2, state.RootElement.GetProperty("revisionId").GetInt64());
        Assert.Equal(2, manager.State.Revision);
        Assert.Equal(0, manager.PendingWaiterCount);
    }

    [Fact]
    public async Task ConcurrentIdenticalAssignmentsCreateOnceAndReplaysDoNotChangeRevision()
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        Assert.True(manager.OnWorkerAttached(1));
        Assert.True(manager.OnWorkerStartStream(1, "worker"));
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<HttpStatusCode>[] assignments = Enumerable.Range(0, 12).Select(async _ =>
        {
            await start.Task.WaitAsync(timeout.Token);
            using StringContent content = JsonBody(ValidAssignment);
            using HttpResponseMessage response = await client.PutAsync(AssignPath, content, timeout.Token);
            Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.NoContent);
            await AssertAssignmentSuccessAsync(response, response.StatusCode, timeout.Token);
            return response.StatusCode;
        }).ToArray();

        start.SetResult();
        HttpStatusCode[] statuses = await Task.WhenAll(assignments).WaitAsync(timeout.Token);
        Assert.Single(statuses, status => status == HttpStatusCode.Created);
        Assert.Equal(statuses.Length - 1, statuses.Count(status => status == HttpStatusCode.NoContent));
        Assert.Equal(3, manager.State.Revision);
        WorkerPodState assigned = manager.State;

        using StringContent replayContent = JsonBody(ValidAssignment);
        using HttpResponseMessage replay = await client.PutAsync(AssignPath, replayContent, timeout.Token);
        await AssertAssignmentSuccessAsync(replay, HttpStatusCode.NoContent, timeout.Token);
        Assert.Same(assigned, manager.State);
    }

    [Theory]
    [InlineData("startupMode", "\"Preconfigured\"", false)]
    [InlineData("functionAppName", "\"TEST-app\"", false)]
    [InlineData("functionGroupName", "\"TEST-group\"", false)]
    [InlineData("functionAppDirectory", "\"/home/site/WWWROOT\"", false)]
    [InlineData("isAlwaysReady", "true", false)]
    [InlineData("environment", "{\"SETTING\":\"PRIVATE-value\"}", false)]
    [InlineData("environment", "{\"setting\":\"private-value\"}", false)]
    [InlineData("startupMode", "\"Preconfigured\"", true)]
    [InlineData("functionAppName", "\"TEST-app\"", true)]
    [InlineData("functionGroupName", "\"TEST-group\"", true)]
    [InlineData("functionAppDirectory", "\"/home/site/WWWROOT\"", true)]
    [InlineData("isAlwaysReady", "true", true)]
    [InlineData("environment", "{\"SETTING\":\"PRIVATE-value\"}", true)]
    [InlineData("environment", "{\"setting\":\"private-value\"}", true)]
    public async Task AllIdentityFieldsConflictEvenAfterTermination(string field, string value, bool terminated)
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        Assert.True(manager.OnWorkerAttached(1));
        Assert.True(manager.OnWorkerStartStream(1, "worker"));
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        using StringContent content = JsonBody(ValidAssignment);
        using HttpResponseMessage created = await client.PutAsync(AssignPath, content, timeout.Token);
        await AssertAssignmentSuccessAsync(created, HttpStatusCode.Created, timeout.Token);
        if (terminated)
        {
            Assert.True(manager.OnSessionTerminated(1));
        }

        WorkerPodState assigned = manager.State;
        JsonObject different = JsonNode.Parse(ValidAssignment)!.AsObject();
        different[field] = JsonNode.Parse(value);
        using StringContent conflictingContent = JsonBody(different.ToJsonString());
        using HttpResponseMessage conflict = await client.PutAsync(AssignPath, conflictingContent, timeout.Token);
        await AssertAssignmentErrorAsync(conflict, HttpStatusCode.Conflict, "AssignmentConflict", timeout.Token);
        Assert.Same(assigned, manager.State);

        using StringContent replayContent = JsonBody(ValidAssignment);
        using HttpResponseMessage replay = await client.PutAsync(AssignPath, replayContent, timeout.Token);
        if (terminated)
        {
            await AssertAssignmentErrorAsync(replay, HttpStatusCode.Conflict, "WorkerTerminated", timeout.Token);
        }
        else
        {
            await AssertAssignmentSuccessAsync(replay, HttpStatusCode.NoContent, timeout.Token);
        }

        Assert.Same(assigned, manager.State);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"SETTING\":\"\"}")]
    public async Task EmptyEnvironmentOrValueIsValidAndNotReadyDoesNotReserveIdentity(string environment)
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        WorkerPodState before = manager.State;
        using StringContent notReadyContent = JsonBody(ValidAssignment);
        using HttpResponseMessage notReady = await client.PutAsync(AssignPath, notReadyContent, timeout.Token);
        await AssertAssignmentErrorAsync(notReady, HttpStatusCode.ServiceUnavailable, "WorkerNotReady", timeout.Token);
        Assert.Same(before, manager.State);
        Assert.Equal(0, manager.State.Revision);
        Assert.Equal(WorkerAssignmentState.Unassigned, manager.State.AssignmentState);
        Assert.Null(manager.State.FunctionAppName);
        Assert.Null(manager.State.StartupMode);

        Assert.True(manager.OnWorkerAttached(1));
        Assert.True(manager.OnWorkerStartStream(1, "worker"));
        JsonObject assignment = JsonNode.Parse(ValidAssignment)!.AsObject();
        assignment["functionAppName"] = "other-app";
        assignment["startupMode"] = "Preconfigured";
        assignment["environment"] = JsonNode.Parse(environment);
        using StringContent content = JsonBody(assignment.ToJsonString());
        using HttpResponseMessage created = await client.PutAsync(AssignPath, content, timeout.Token);
        await AssertAssignmentSuccessAsync(created, HttpStatusCode.Created, timeout.Token);
        Assert.Equal(3, manager.State.Revision);
        Assert.Equal("other-app", manager.State.FunctionAppName);
        Assert.Equal(WorkerStartupMode.Preconfigured, manager.State.StartupMode);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public async Task PreconfiguredAssignmentPreservesEmptyOrWhitespaceDirectory(string directory)
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        Assert.True(manager.OnWorkerAttached(1));
        Assert.True(manager.OnWorkerStartStream(1, "worker"));
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        JsonObject body = JsonNode.Parse(ValidAssignment)!.AsObject();
        body["startupMode"] = "Preconfigured";
        body["functionAppDirectory"] = directory;
        body["environment"] = new JsonObject();
        using StringContent content = JsonBody(body.ToJsonString());
        using HttpResponseMessage response = await client.PutAsync(AssignPath, content, timeout.Token);

        await AssertAssignmentSuccessAsync(response, HttpStatusCode.Created, timeout.Token);
        WorkerPodState assigned = manager.State;
        Assert.Equal(WorkerStartupMode.Preconfigured, assigned.StartupMode);
        Assert.Equal(WorkerAssignmentState.Ready, assigned.AssignmentState);
        Assert.Equal(WorkerPodStatus.ReadyForRequest, assigned.PodStatus);
        Assert.Equal(3, assigned.Revision);

        using StringContent replayContent = JsonBody(body.ToJsonString());
        using HttpResponseMessage replay = await client.PutAsync(AssignPath, replayContent, timeout.Token);
        await AssertAssignmentSuccessAsync(replay, HttpStatusCode.NoContent, timeout.Token);
        Assert.Same(assigned, manager.State);

        body["functionAppDirectory"] = string.Equals(directory, string.Empty, StringComparison.Ordinal) ? " " : string.Empty;
        using StringContent changedContent = JsonBody(body.ToJsonString());
        using HttpResponseMessage changed = await client.PutAsync(AssignPath, changedContent, timeout.Token);
        await AssertAssignmentErrorAsync(changed, HttpStatusCode.Conflict, "AssignmentConflict", timeout.Token);
        Assert.Same(assigned, manager.State);

        using HttpResponseMessage current = await client.GetAsync(StatePath, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        using JsonDocument state = JsonDocument.Parse(await current.Content.ReadAsStringAsync(timeout.Token));
        Assert.Equal("Preconfigured", state.RootElement.GetProperty("workerPodState").GetProperty("startupMode").GetString());
    }

    private static void AssertNoStore(HttpResponseMessage response) =>
        Assert.True(response.Headers.CacheControl?.NoStore);

    private static async Task AssertAssignmentSuccessAsync(
        HttpResponseMessage response, HttpStatusCode statusCode, CancellationToken cancellationToken)
    {
        Assert.Equal(statusCode, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(cancellationToken));
        if (statusCode == HttpStatusCode.Created)
        {
            Assert.Equal(AssignPath, response.Headers.Location?.OriginalString);
        }
    }

    private static async Task AssertAssignmentErrorAsync(
        HttpResponseMessage response, HttpStatusCode statusCode, string code, CancellationToken cancellationToken)
    {
        Assert.Equal(statusCode, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("error", Assert.Single(json.RootElement.EnumerateObject()).Name);
        Assert.Equal(code, json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private static StringContent JsonBody(string body) => new(body, Encoding.UTF8, "application/json");

    private static async Task AssertValidationAsync(
        HttpResponseMessage response, CancellationToken cancellationToken, params (string Code, string Target)[] expected)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument json = JsonDocument.Parse(body);
        Assert.Equal("errors", Assert.Single(json.RootElement.EnumerateObject()).Name);
        JsonElement errors = json.RootElement.GetProperty("errors");
        Assert.Equal(expected.Length, errors.GetArrayLength());
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.Equal(new[] { "code", "target" }, errors[index].EnumerateObject().Select(property => property.Name).Order());
            Assert.Equal(expected[index].Code, errors[index].GetProperty("code").GetString());
            Assert.Equal(expected[index].Target, errors[index].GetProperty("target").GetString());
        }

        Assert.DoesNotContain("private-value", body);
    }
}
