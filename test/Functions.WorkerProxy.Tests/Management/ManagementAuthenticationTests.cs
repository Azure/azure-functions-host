// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Authentication;
using Azure.Functions.Tests;
using Azure.Functions.WorkerProxy.State;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests.Management;

public class ManagementAuthenticationTests
{
    [Fact]
    public async Task MissingSiteHeader_DoesNotAttemptManagementAuthenticationForOtherProtocols()
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        DefaultHttpContext context = new() { RequestServices = scope.ServiceProvider };
        context.Request.Path = "/AzureFunctionsRpcMessages.FunctionRpc/EventStream";

        AuthenticateResult result = await context.AuthenticateAsync(ContainerManagementAuthenticationHandler.SchemeName);

        Assert.True(result.None);
        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("missing", false)]
    [InlineData("malformed", false)]
    [InlineData("wrong-key", false)]
    [InlineData("app-key", false)]
    [InlineData("caller-token", false)]
    [InlineData("wrong-pod", false)]
    [InlineData("wrong-issuer", false)]
    [InlineData("wrong-algorithm", false)]
    [InlineData("expired", false)]
    [InlineData("future", false)]
    [InlineData("duplicate", false)]
    public async Task Assignment_RequiresContainerManagementTokenBeforeAnyWorkerOrAppConfiguration(string scenario, bool accepted)
    {
        await using WorkerProxyWebApplicationFactory factory = new(
            new Dictionary<string, string?> { ["WEBSITE_AUTH_ENCRYPTION_KEY"] = null }, useTestAuthentication: false);
        using HttpClient client = factory.CreateWorkerProxyClient(authenticate: false);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string token = scenario switch
        {
            "malformed" => "invalid-token",
            "wrong-key" => ManagementTokenTestData.Create(ManagementTokenTestData.RuntimeKey, RuntimeAuthenticationTestData.WorkerId),
            "app-key" => ManagementTokenTestData.Create(RuntimeAuthenticationTestData.EncodedAppKey, RuntimeAuthenticationTestData.WorkerId),
            "caller-token" => RuntimeAuthenticationTestData.CreateCallerToken(),
            "wrong-pod" => ManagementTokenTestData.Create(RuntimeAuthenticationTestData.EncodedWorkerKey, "another-pod"),
            "wrong-issuer" => ManagementTokenTestData.Create(RuntimeAuthenticationTestData.EncodedWorkerKey,
                RuntimeAuthenticationTestData.WorkerId, issuer: "https://appservice.core.azurewebsites.net"),
            "wrong-algorithm" => ManagementTokenTestData.Create(RuntimeAuthenticationTestData.EncodedWorkerKey,
                RuntimeAuthenticationTestData.WorkerId, algorithm: "HS512"),
            "expired" => ManagementTokenTestData.Create(RuntimeAuthenticationTestData.EncodedWorkerKey,
                RuntimeAuthenticationTestData.WorkerId, notBefore: now.AddMinutes(-20), expires: now.AddMinutes(-6)),
            "future" => ManagementTokenTestData.Create(RuntimeAuthenticationTestData.EncodedWorkerKey,
                RuntimeAuthenticationTestData.WorkerId, notBefore: now.AddMinutes(6), expires: now.AddMinutes(10)),
            _ => ManagementTokenTestData.Create(RuntimeAuthenticationTestData.EncodedWorkerKey, RuntimeAuthenticationTestData.WorkerId)
        };
        if (!string.Equals(scenario, "missing", StringComparison.Ordinal))
        {
            client.DefaultRequestHeaders.Add(ManagementTokenTestData.HeaderName,
                string.Equals(scenario, "duplicate", StringComparison.Ordinal) ? [token, token] : [token]);
        }

        const string body = """
            {"startupMode":"Preconfigured","functionAppName":"test-app","functionGroupName":"group","isAlwaysReady":false,
             "environment":{"PRIVATE_SETTING":"never-published"},"functionAppDirectory":"","httpProxyEndpoint":"127.0.0.1:48801"}
            """;
        using StringContent content = new(body, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PutAsync("/admin/worker/assignment", content, timeout.Token);

        Assert.Equal(accepted ? HttpStatusCode.Created : HttpStatusCode.Unauthorized, response.StatusCode);
        WorkerPodState state = factory.Services.GetRequiredService<WorkerPodStateManager>().State;
        Assert.Equal(accepted ? WorkerAssignmentState.Ready : WorkerAssignmentState.Unassigned, state.AssignmentState);
        Assert.False(state.IsWorkerReady);
        Assert.Null(state.SessionId);
        Assert.DoesNotContain(token, await response.Content.ReadAsStringAsync(timeout.Token), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/admin/instance/ready")]
    [InlineData("/admin/worker/ready")]
    [InlineData("/admin/worker/state")]
    public async Task LegacyReadinessAndStateRoutes_AreNotAvailable(string path)
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));

        using HttpResponseMessage response = await client.GetAsync(path, timeout.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("127.0.0.1:48801", "https://127.0.0.1:48801/")]
    [InlineData("[::1]:48801", "https://[::1]:48801/")]
    [InlineData("https://127.0.0.1:48801/", "https://127.0.0.1:48801/")]
    public async Task Assignment_RecordsAllocatedHttpsOriginBeforeWorkerAndIncludesItInRetryIdentity(string value, string expected)
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        string body = $$"""
            {"startupMode":"Preconfigured","functionAppName":"test-app","functionGroupName":"group","isAlwaysReady":false,
             "environment":{},"functionAppDirectory":"","httpProxyEndpoint":"{{value}}"}
            """;
        using StringContent content = new(body, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PutAsync("/admin/worker/assignment", content, timeout.Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        Assert.Equal(expected, manager.State.HttpProxyEndpoint?.AbsoluteUri);
        Assert.False(manager.State.IsWorkerAttached);

        using StringContent replayContent = new(body, Encoding.UTF8, "application/json");
        using HttpResponseMessage replay = await client.PutAsync("/admin/worker/assignment", replayContent, timeout.Token);
        Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        string different = body.Replace("48801", "48802", StringComparison.Ordinal);
        using StringContent conflictContent = new(different, Encoding.UTF8, "application/json");
        using HttpResponseMessage conflict = await client.PutAsync("/admin/worker/assignment", conflictContent, timeout.Token);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(expected, manager.State.HttpProxyEndpoint?.AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("127.0.0.1")]
    [InlineData("http://127.0.0.1:48801")]
    [InlineData("127.0.0.1:0")]
    [InlineData("127.0.0.1:65536")]
    [InlineData("https://user@127.0.0.1:48801")]
    [InlineData("https://127.0.0.1:48801/customer")]
    [InlineData("https://127.0.0.1:48801/?query=1")]
    [InlineData("0.0.0.0:48801")]
    public async Task Assignment_InvalidAllocatedOriginCannotClaimIdentity(string value)
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        string body = $$"""
            {"startupMode":"Preconfigured","functionAppName":"test-app","functionGroupName":"group","isAlwaysReady":false,
             "environment":{},"functionAppDirectory":"","httpProxyEndpoint":"{{value}}"}
            """;
        using StringContent content = new(body, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await client.PutAsync("/admin/worker/assignment", content, timeout.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("httpProxyEndpoint", await response.Content.ReadAsStringAsync(timeout.Token), StringComparison.Ordinal);
        Assert.Equal(WorkerAssignmentState.Unassigned, factory.Services.GetRequiredService<WorkerPodStateManager>().State.AssignmentState);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("app-key")]
    [InlineData("runtime-key")]
    [InlineData("caller-token")]
    public async Task InitialEmptyStatePoll_RejectsMissingOrWrongRoleManagementProof(string scenario)
    {
        await using WorkerProxyWebApplicationFactory factory = new(useTestAuthentication: false);
        using HttpClient client = factory.CreateWorkerProxyClient(authenticate: false);
        using HttpRequestMessage request = new(HttpMethod.Post, "/admin/infra/instanceState");
        request.Headers.Add("x-ms-functions-pod-name", RuntimeAuthenticationTestData.WorkerId);
        string? token = scenario switch
        {
            "app-key" => ManagementTokenTestData.Create(RuntimeAuthenticationTestData.EncodedAppKey, RuntimeAuthenticationTestData.WorkerId),
            "runtime-key" => ManagementTokenTestData.Create(ManagementTokenTestData.RuntimeKey, RuntimeAuthenticationTestData.WorkerId),
            "caller-token" => RuntimeAuthenticationTestData.CreateCallerToken(),
            _ => null
        };
        if (token is not null)
        {
            request.Headers.Add(ManagementTokenTestData.HeaderName, token);
        }

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, factory.Services.GetRequiredService<WorkerPodStateManager>().State.Revision);
    }
}
