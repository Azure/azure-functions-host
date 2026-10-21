// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.ManagedIdentity;
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Google.Protobuf;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    private const string TokenRequestPath =
        $"{ManagedIdentityEndpoint.Path}?resource=https%3A%2F%2Fstorage.azure.com%2F&api-version=2017-09-01&clientid=client-id";

    [Fact]
    public async Task ManagedIdentityStartup_AssignmentBeforeStartStreamRelaysUnsignedRequestAndBecomesReadyAfterStartStream()
    {
        const string secret = "worker-local-secret";
        const string responseBody =
            """{"access_token":"startup-token","token_type":"Bearer","expires_on":"4102444800","resource":"https://storage.azure.com/","client_id":"client-id"}""";
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using HttpClient managedIdentity = factory.CreateManagedIdentityClient();
        using CancellationTokenSource timeout = new(TestTimeout);

        await AssignManagedIdentityAsync(management, secret, timeout.Token);

        Assert.Equal(0, manager.State.Revision);
        Assert.Equal(WorkerAssignmentState.Unassigned, manager.State.AssignmentState);
        Assert.False(manager.State.IsWorkerReady);
        await AssertReadinessAsync(management, "/admin/worker/ready", HttpStatusCode.ServiceUnavailable, timeout.Token);

        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);
        Task<HttpResponseMessage> tokenTask = SendTokenRequestAsync(
            managedIdentity,
            TokenRequestPath,
            secret,
            timeout.Token);
        StreamingMessage tokenRequest = await runtime.ReadAsync(timeout.Token);
        Assert.False(string.IsNullOrWhiteSpace(tokenRequest.RequestId));
        Assert.Equal(StreamingMessage.ContentOneofCase.ManagedIdentityTokenRequest, tokenRequest.ContentCase);
        Assert.Equal("https://storage.azure.com/", tokenRequest.ManagedIdentityTokenRequest.Resource);
        Assert.Equal("client-id", tokenRequest.ManagedIdentityTokenRequest.ClientId);
        Assert.Equal("2017-09-01", tokenRequest.ManagedIdentityTokenRequest.ApiVersion);
        Assert.True(tokenRequest.ManagedIdentityTokenRequest.RuntimeAccessKeySignature.IsEmpty);

        await WriteTokenResponseAsync(runtime, tokenRequest, HttpStatusCode.OK, responseBody, timeout.Token);
        using (HttpResponseMessage token = await tokenTask)
        {
            Assert.Equal(HttpStatusCode.OK, token.StatusCode);
            Assert.Equal(responseBody, await token.Content.ReadAsStringAsync(timeout.Token));
        }

        Assert.False(manager.State.IsWorkerReady);
        await AssertReadinessAsync(management, "/admin/worker/ready", HttpStatusCode.ServiceUnavailable, timeout.Token);

        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        StreamingMessage start = CreateStartStream();
        await worker.WriteAsync(start, timeout.Token);
        Assert.Equal(start, await runtime.ReadAsync(timeout.Token));
        await WaitForWorkerReadyAsync(manager, timeout.Token);

        Assert.Equal(WorkerAssignmentState.Ready, manager.State.AssignmentState);
        Assert.Equal(WorkerPodStatus.ReadyForRequest, manager.State.PodStatus);
        await AssertReadinessAsync(management, "/admin/worker/ready", HttpStatusCode.OK, timeout.Token);
    }

    [Fact]
    public async Task ManagedIdentityEndpoint_InvalidWorkerSecretReturnsForbiddenWithoutRuntimeRelay()
    {
        const string secret = "worker-local-secret";
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using HttpClient managedIdentity = factory.CreateManagedIdentityClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        await AssignManagedIdentityAsync(management, secret, timeout.Token);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);

        using (HttpResponseMessage forbidden = await SendTokenRequestAsync(
            managedIdentity,
            TokenRequestPath,
            "wrong-secret",
            timeout.Token))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        string validPath =
            $"{ManagedIdentityEndpoint.Path}?resource=valid-request&api-version=2017-09-01";
        Task<HttpResponseMessage> validTask = SendTokenRequestAsync(
            managedIdentity,
            validPath,
            secret,
            timeout.Token);
        StreamingMessage validRequest = await runtime.ReadAsync(timeout.Token);
        Assert.Equal("valid-request", validRequest.ManagedIdentityTokenRequest.Resource);
        await WriteTokenResponseAsync(
            runtime,
            validRequest,
            HttpStatusCode.OK,
            CreateTokenBody("valid-token", "valid-request"),
            timeout.Token);

        using HttpResponseMessage validResponse = await validTask;
        Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);
    }

    [Fact]
    public async Task ManagedIdentityEndpoint_MissingOrNonResponsiveRuntimeReturnsRetryableServiceUnavailable()
    {
        const string secret = "worker-local-secret";
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using HttpClient managedIdentity = factory.CreateManagedIdentityClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        await AssignManagedIdentityAsync(management, secret, timeout.Token);

        using (HttpResponseMessage missingRuntime = await SendTokenRequestAsync(
            managedIdentity,
            TokenRequestPath,
            secret,
            timeout.Token))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, missingRuntime.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(1), missingRuntime.Headers.RetryAfter?.Delta);
        }

        await using (RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token))
        {
            await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);
            using CancellationTokenSource callerCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            Task<HttpResponseMessage> canceledTask = SendTokenRequestAsync(
                managedIdentity,
                $"{ManagedIdentityEndpoint.Path}?resource=canceled&api-version=2017-09-01",
                secret,
                callerCancellation.Token);
            await runtime.ReadAsync(timeout.Token);
            callerCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledTask);

            Task<HttpResponseMessage> disconnectedTask = SendTokenRequestAsync(
                managedIdentity,
                $"{ManagedIdentityEndpoint.Path}?resource=disconnected&api-version=2017-09-01",
                secret,
                timeout.Token);
            await runtime.ReadAsync(timeout.Token);
            await runtime.CompleteRequestAsync(timeout.Token);
            using HttpResponseMessage disconnected = await disconnectedTask;
            Assert.Equal(HttpStatusCode.ServiceUnavailable, disconnected.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(1), disconnected.Headers.RetryAfter?.Delta);
            await runtime.WaitForTerminationAsync(timeout.Token);
        }

        await WaitForReleaseAsync(relay, timeout.Token);
        await using RelayClient replacementRuntime =
            CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);
        long started = Stopwatch.GetTimestamp();
        Task<HttpResponseMessage> nonResponsiveTask = SendTokenRequestAsync(
            managedIdentity,
            TokenRequestPath,
            secret,
            timeout.Token);
        await replacementRuntime.ReadAsync(timeout.Token);

        using HttpResponseMessage nonResponsive = await nonResponsiveTask;
        TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, nonResponsive.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(1), nonResponsive.Headers.RetryAfter?.Delta);
        Assert.InRange(elapsed, TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task ManagedIdentityEndpoint_CacheHitIsScopedToRuntimeSession()
    {
        const string secret = "worker-local-secret";
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using HttpClient managedIdentity = factory.CreateManagedIdentityClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        await AssignManagedIdentityAsync(management, secret, timeout.Token);

        await using (RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token))
        {
            await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);
            Task<HttpResponseMessage> firstTask = SendTokenRequestAsync(
                managedIdentity,
                TokenRequestPath,
                secret,
                timeout.Token);
            StreamingMessage firstRequest = await runtime.ReadAsync(timeout.Token);
            await WriteTokenResponseAsync(
                runtime,
                firstRequest,
                HttpStatusCode.OK,
                CreateTokenBody("first-token", "https://storage.azure.com/"),
                timeout.Token);

            using (HttpResponseMessage first = await firstTask)
            {
                Assert.Equal("first-token", await ReadAccessTokenAsync(first, timeout.Token));
            }

            using (HttpResponseMessage cached = await SendTokenRequestAsync(
                managedIdentity,
                TokenRequestPath,
                secret,
                timeout.Token))
            {
                Assert.Equal("first-token", await ReadAccessTokenAsync(cached, timeout.Token));
            }

            string uncachedPath =
                $"{ManagedIdentityEndpoint.Path}?resource=uncached&api-version=2017-09-01";
            Task<HttpResponseMessage> uncachedTask = SendTokenRequestAsync(
                managedIdentity,
                uncachedPath,
                secret,
                timeout.Token);
            StreamingMessage uncachedRequest = await runtime.ReadAsync(timeout.Token);
            Assert.Equal("uncached", uncachedRequest.ManagedIdentityTokenRequest.Resource);
            await WriteTokenResponseAsync(
                runtime,
                uncachedRequest,
                HttpStatusCode.OK,
                CreateTokenBody("uncached-token", "uncached"),
                timeout.Token);
            using HttpResponseMessage uncached = await uncachedTask;
            Assert.Equal("uncached-token", await ReadAccessTokenAsync(uncached, timeout.Token));

            await runtime.CompleteRequestAsync(timeout.Token);
            await runtime.WaitForTerminationAsync(timeout.Token);
        }

        await WaitForReleaseAsync(relay, timeout.Token);
        await using RelayClient replacementRuntime =
            CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);
        Task<HttpResponseMessage> replacementTask = SendTokenRequestAsync(
            managedIdentity,
            TokenRequestPath,
            secret,
            timeout.Token);
        StreamingMessage replacementRequest = await replacementRuntime.ReadAsync(timeout.Token);
        Assert.Equal("https://storage.azure.com/", replacementRequest.ManagedIdentityTokenRequest.Resource);
        await WriteTokenResponseAsync(
            replacementRuntime,
            replacementRequest,
            HttpStatusCode.OK,
            CreateTokenBody("replacement-token", "https://storage.azure.com/"),
            timeout.Token);

        using HttpResponseMessage replacement = await replacementTask;
        Assert.Equal("replacement-token", await ReadAccessTokenAsync(replacement, timeout.Token));
    }

    private static async Task AssignManagedIdentityAsync(
        HttpClient client,
        string secret,
        CancellationToken cancellationToken)
    {
        string assignmentBody = JsonSerializer.Serialize(new
        {
            startupMode = "Preconfigured",
            functionAppName = "test-app",
            functionGroupName = "test-group",
            isAlwaysReady = false,
            environment = new Dictionary<string, string> { ["MSI_SECRET"] = secret },
            functionAppDirectory = string.Empty
        });
        using StringContent content = new(assignmentBody, Encoding.UTF8, "application/json");
        using HttpResponseMessage assignment = await client.PutAsync(
            "/admin/worker/assignment",
            content,
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, assignment.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendTokenRequestAsync(
        HttpClient client,
        string requestPath,
        string secret,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, requestPath);
        request.Headers.TryAddWithoutValidation("Secret", secret);
        return await client.SendAsync(request, cancellationToken);
    }

    private static async Task WriteTokenResponseAsync(
        RelayClient runtime,
        StreamingMessage request,
        HttpStatusCode statusCode,
        string body,
        CancellationToken cancellationToken)
    {
        await runtime.WriteAsync(new()
        {
            RequestId = request.RequestId,
            ManagedIdentityTokenResponse = new()
            {
                StatusCode = (int)statusCode,
                ContentType = "application/json",
                Body = ByteString.CopyFromUtf8(body),
                Result = new() { Status = StatusResult.Types.Status.Success }
            }
        }, cancellationToken);
    }

    private static string CreateTokenBody(string accessToken, string resource)
        => $$"""{"access_token":"{{accessToken}}","token_type":"Bearer","expires_on":"4102444800","resource":"{{resource}}","client_id":""}""";

    private static async Task<string?> ReadAccessTokenAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return body.RootElement.GetProperty("access_token").GetString();
    }

    private static async Task WaitForWorkerReadyAsync(
        WorkerPodStateManager manager,
        CancellationToken cancellationToken)
    {
        while (!manager.State.IsWorkerReady)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }
}
