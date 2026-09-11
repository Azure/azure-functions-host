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
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests.Management;

public class ManagementApiEndpointTests
{
    private const string AssignPath = "/admin/worker/assign";
    private const string StatePath = "/admin/infra/instanceState";
    private const string ValidAssignment = """
        {"functionAppName":"test-app","functionGroupName":"test-group","isAlwaysReady":false,
         "environment":{"SETTING":"private-value"},"functionAppDirectory":"/home/site/wwwroot"}
        """;

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"lastKnownRevision\":null}")]
    public async Task InitialPollReturnsRevisionZeroImmediately(string body)
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        using StringContent content = JsonBody(body);
        using HttpResponseMessage response = await client.PostAsync(StatePath, content, timeout.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument state = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        Assert.Equal(0, state.RootElement.GetProperty("revisionId").GetInt64());
        Assert.Equal("None", state.RootElement.GetProperty("workerPodState").GetProperty("podStatus").GetString());
        Assert.Equal(0, factory.Services.GetRequiredService<WorkerPodStateManager>().PendingWaiterCount);
    }

    [Theory]
    [InlineData(AssignPath, "")]
    [InlineData(AssignPath, "null")]
    [InlineData(AssignPath, "[]")]
    [InlineData(AssignPath, "{")]
    [InlineData(StatePath, "")]
    [InlineData(StatePath, "null")]
    [InlineData(StatePath, "[]")]
    [InlineData(StatePath, "{")]
    [InlineData(StatePath, "{\"lastKnownRevision\":\"0\"}")]
    [InlineData(StatePath, "{\"lastKnownRevision\":1.5}")]
    [InlineData(StatePath, "{\"lastKnownRevision\":9223372036854775808}")]
    public async Task MalformedBodyUsesHostValidationEnvelope(string path, string body)
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        using StringContent content = JsonBody(body);
        using HttpResponseMessage response = await client.PostAsync(path, content, timeout.Token);

        await AssertValidationAsync(response, timeout.Token, ("InvalidBody", "request"));
        Assert.Equal(0, factory.Services.GetRequiredService<WorkerPodStateManager>().State.Revision);
    }

    [Theory]
    [InlineData("functionAppName", null, "Required")]
    [InlineData("functionAppName", "\" \"", "Required")]
    [InlineData("functionGroupName", null, "Required")]
    [InlineData("functionGroupName", "\"\"", "Required")]
    [InlineData("functionAppDirectory", null, "Required")]
    [InlineData("functionAppDirectory", "\" \"", "Required")]
    [InlineData("isAlwaysReady", null, "Required")]
    [InlineData("isAlwaysReady", "null", "Required")]
    [InlineData("isAlwaysReady", "\"false\"", "InvalidBody")]
    [InlineData("environment", null, "Required")]
    [InlineData("environment", "null", "Required")]
    [InlineData("environment", "{\"\":\"private-value\"}", "InvalidValue")]
    [InlineData("environment", "{\"SETTING\":null}", "InvalidValue")]
    [InlineData("environment", "{\"SETTING\":123}", "InvalidBody")]
    public async Task InvalidAssignmentDoesNotClaimIdentity(string field, string? value, string code)
    {
        JsonObject body = JsonNode.Parse(ValidAssignment)!.AsObject();
        if (value is null)
        {
            body.Remove(field);
        }
        else
        {
            body[field] = JsonNode.Parse(value);
        }

        await using WorkerProxyWebApplicationFactory factory = new();
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        using StringContent content = JsonBody(body.ToJsonString());
        using HttpResponseMessage response = await client.PostAsync(AssignPath, content, timeout.Token);

        await AssertValidationAsync(response, timeout.Token, (code, code == "InvalidBody" ? "request" : field));
        WorkerPodState state = factory.Services.GetRequiredService<WorkerPodStateManager>().State;
        Assert.Equal(0, state.Revision);
        Assert.Equal(WorkerAssignmentState.Unassigned, state.AssignmentState);
        Assert.Null(state.FunctionAppName);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""
        {"functionAppName":" ","functionGroupName":"","isAlwaysReady":null,
         "functionAppDirectory":" ","environment":{"PRIVATE_SETTING":null}}
        """)]
    public async Task AssignmentReturnsAllInvalidFieldsInOneResponse(string body)
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        using StringContent content = JsonBody(body);
        using HttpResponseMessage response = await client.PostAsync(AssignPath, content, timeout.Token);

        await AssertValidationAsync(response, timeout.Token,
            ("Required", "functionAppName"),
            ("Required", "functionGroupName"),
            ("Required", "isAlwaysReady"),
            ("Required", "functionAppDirectory"),
            (body == "{}" ? "Required" : "InvalidValue", "environment"));
        string json = await response.Content.ReadAsStringAsync(timeout.Token);
        Assert.DoesNotContain("PRIVATE_SETTING", json);
        Assert.Equal(0, factory.Services.GetRequiredService<WorkerPodStateManager>().State.Revision);
    }

    [Theory]
    [InlineData(AssignPath)]
    [InlineData(StatePath)]
    public async Task NonJsonContentUsesHostValidationEnvelope(string path)
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        using StringContent content = new("{}", Encoding.UTF8, "text/plain");
        using HttpResponseMessage response = await client.PostAsync(path, content, timeout.Token);

        await AssertValidationAsync(response, timeout.Token, ("InvalidBody", "request"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(long.MaxValue)]
    public async Task InvalidRevisionUsesHostValidationEnvelope(long revision)
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        using StringContent content = JsonBody($"{{\"lastKnownRevision\":{revision}}}");
        using HttpResponseMessage response = await client.PostAsync(StatePath, content, timeout.Token);

        await AssertValidationAsync(response, timeout.Token, ("InvalidRevision", "lastKnownRevision"));
        Assert.Equal(0, factory.Services.GetRequiredService<WorkerPodStateManager>().PendingWaiterCount);
    }

    [Theory]
    [InlineData("/admin/worker/ready", "GET")]
    [InlineData(AssignPath, "POST")]
    [InlineData(StatePath, "POST")]
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
                Content = method == "POST" ? JsonBody("{}") : null
            };
            using HttpResponseMessage response = await rpcClient.SendAsync(request, timeout.Token);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using HttpClient forwardingClient = factory.CreateHttpForwardingClient();
        using HttpRequestMessage forwardingRequest = new(new HttpMethod(method), path)
        {
            Content = method == "POST" ? JsonBody("{}") : null
        };
        using HttpResponseMessage forwardingResponse = await forwardingClient.SendAsync(forwardingRequest, timeout.Token);
        Assert.Equal(HttpStatusCode.NotFound, forwardingResponse.StatusCode);
    }

    [Theory]
    [InlineData("/admin/worker/ready", "POST")]
    [InlineData(AssignPath, "GET")]
    [InlineData(StatePath, "GET")]
    public async Task ManagementRoutesRejectUnsupportedMethods(string path, string method)
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpRequestMessage request = new(new HttpMethod(method), path);
        using HttpResponseMessage response = await client.SendAsync(request, timeout.Token);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task EqualRevisionPollReturnsNoContentAtDeadline()
    {
        Mock<TimeProvider> clock = new();
        Mock<ITimer> timer = new();
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
        using StringContent content = JsonBody("{\"lastKnownRevision\":0}");
        Task<HttpResponseMessage> poll = client.PostAsync(StatePath, content, timeout.Token);
        Action fireTimer = await expire.Task.WaitAsync(timeout.Token);
        Assert.False(poll.IsCompleted);

        fireTimer();
        using HttpResponseMessage response = await poll;
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(timeout.Token));
        Assert.Equal(0, factory.Services.GetRequiredService<WorkerPodStateManager>().PendingWaiterCount);
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
        using StringContent content = JsonBody("{\"lastKnownRevision\":0}");
        Task<HttpResponseMessage> poll = client.PostAsync(StatePath, content, cancellation.Token);
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
