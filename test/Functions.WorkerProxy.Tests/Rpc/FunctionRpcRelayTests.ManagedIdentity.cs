// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.ManagedIdentity;
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    private const string TokenRequestPath =
        $"{ManagedIdentityEndpoint.Path}?resource=https%3A%2F%2Fstorage.azure.com%2F&api-version=2019-08-01&client_id=client-id";

    [Fact]
    public async Task ManagedIdentityRequests_AreBoundedAndCanceledWhenRelayEnds()
    {
        WorkerPodStateManager manager = CreatePodStateManager();
        Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(CreateWorkerAssignment()));
        await using FunctionRpcRelay relay = CreateInProcessRelay(manager);
        using CancellationTokenSource timeout = new(TestTimeout);
        Channel<StreamingMessage> runtimeInbound = Channel.CreateUnbounded<StreamingMessage>();
        Channel<StreamingMessage> runtimeOutbound = Channel.CreateUnbounded<StreamingMessage>();
        Task<FunctionRpcRelayTerminalState> runtimeTask = relay.AttachAsync(
            FunctionRpcRelaySide.Runtime,
            CreateMessageReader(runtimeInbound.Reader),
            CreateMessageWriter(runtimeOutbound.Writer),
            timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);

        Task<ManagedIdentityTokenResponse>[] pending = new Task<ManagedIdentityTokenResponse>[16];
        for (int i = 0; i < pending.Length; i++)
        {
            pending[i] = relay.RequestManagedIdentityTokenAsync(new() { Resource = $"resource-{i}" }, timeout.Token);
            StreamingMessage request = await runtimeOutbound.Reader.ReadAsync(timeout.Token);
            Assert.Equal(StreamingMessage.ContentOneofCase.ManagedIdentityTokenRequest, request.ContentCase);
        }

        await Assert.ThrowsAsync<ManagedIdentityTokenUnavailableException>(() =>
            relay.RequestManagedIdentityTokenAsync(new() { Resource = "overflow" }, timeout.Token));

        await relay.StopAsync(timeout.Token);
        await runtimeTask.WaitAsync(timeout.Token);
        foreach (Task<ManagedIdentityTokenResponse> request in pending)
        {
            await Assert.ThrowsAsync<ManagedIdentityTokenUnavailableException>(() => request);
        }
    }

    [Theory]
    [InlineData("MSI_SECRET", "Secret")]
    [InlineData("IDENTITY_HEADER", "X-IDENTITY-HEADER")]
    public async Task ManagedIdentityEndpoint_UsesAssignedSecretAndExistingRuntimeStream(
        string environmentName,
        string headerName)
    {
        const string secret = "worker-local-secret";
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using HttpClient managedIdentity = factory.CreateManagedIdentityClient();
        using CancellationTokenSource timeout = new(TestTimeout);

        using (HttpResponseMessage unassigned = await SendTokenRequestAsync(
            managedIdentity, headerName, secret, timeout.Token))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unassigned.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(1), unassigned.Headers.RetryAfter?.Delta);
        }

        await AssignAsync(management, new Dictionary<string, string> { [environmentName] = secret }, timeout.Token);

        Assert.Equal(WorkerAssignmentState.Assigned, manager.State.AssignmentState);
        Assert.False(manager.State.IsWorkerReady);

        using (HttpResponseMessage forbidden = await SendTokenRequestAsync(
            managedIdentity, headerName, "wrong-secret", timeout.Token))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        using (HttpResponseMessage noRuntime = await SendTokenRequestAsync(
            managedIdentity, headerName, secret, timeout.Token))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, noRuntime.StatusCode);
        }

        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);

        Task<HttpResponseMessage> tokenTask = SendTokenRequestAsync(
            managedIdentity, headerName, secret, timeout.Token);
        StreamingMessage tokenRequest = await runtime.ReadAsync(timeout.Token);
        Assert.Equal(StreamingMessage.ContentOneofCase.ManagedIdentityTokenRequest, tokenRequest.ContentCase);
        Assert.Equal("https://storage.azure.com/", tokenRequest.ManagedIdentityTokenRequest.Resource);
        Assert.Equal("client-id", tokenRequest.ManagedIdentityTokenRequest.ClientId);
        Assert.Equal("2019-08-01", tokenRequest.ManagedIdentityTokenRequest.ApiVersion);

        await WriteTokenResponseAsync(runtime, tokenRequest, "dummy-token", timeout.Token);

        using HttpResponseMessage token = await tokenTask;
        Assert.Equal(HttpStatusCode.OK, token.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await token.Content.ReadAsStringAsync(timeout.Token));
        Assert.Equal("dummy-token", body.RootElement.GetProperty("access_token").GetString());
        Assert.Equal("Bearer", body.RootElement.GetProperty("token_type").GetString());
        Assert.Equal("4102444800", body.RootElement.GetProperty("expires_on").GetString());
        Assert.Equal("https://storage.azure.com/", body.RootElement.GetProperty("resource").GetString());
        Assert.Equal("client-id", body.RootElement.GetProperty("client_id").GetString());
        Assert.False(manager.State.IsWorkerReady);
        Assert.Equal(WorkerAssignmentState.Assigned, manager.State.AssignmentState);
    }

    [Theory]
    [InlineData("", "/msi/token?resource=resource&api-version=2019-08-01", HttpStatusCode.ServiceUnavailable)]
    [InlineData("secret", "/msi/token?api-version=2019-08-01", HttpStatusCode.BadRequest)]
    [InlineData("secret", "/msi/token?resource=one&resource=two&api-version=2019-08-01", HttpStatusCode.BadRequest)]
    [InlineData("secret", "/msi/token?resource=resource&api-version=2019-08-01&client_id=one&client_id=two",
        HttpStatusCode.BadRequest)]
    public async Task ManagedIdentityEndpoint_RejectsInvalidConfigurationOrRequest(
        string assignedSecret,
        string requestPath,
        HttpStatusCode expectedStatus)
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using HttpClient managedIdentity = factory.CreateManagedIdentityClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        await AssignAsync(
            management,
            new Dictionary<string, string> { ["MSI_SECRET"] = assignedSecret },
            timeout.Token);

        using HttpResponseMessage response = await SendTokenRequestAsync(
            managedIdentity,
            requestPath,
            "Secret",
            assignedSecret,
            timeout.Token);

        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Fact]
    public async Task ManagedIdentityEndpoint_CorrelatesConcurrentResponses()
    {
        const string secret = "worker-local-secret";
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using HttpClient managedIdentity = factory.CreateManagedIdentityClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        await AssignAsync(
            management,
            new Dictionary<string, string> { ["MSI_SECRET"] = secret },
            timeout.Token);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);

        Task<HttpResponseMessage> firstTask = SendTokenRequestAsync(
            managedIdentity,
            $"{ManagedIdentityEndpoint.Path}?resource=first&api-version=2019-08-01",
            "Secret",
            secret,
            timeout.Token);
        StreamingMessage firstRequest = await runtime.ReadAsync(timeout.Token);
        Task<HttpResponseMessage> secondTask = SendTokenRequestAsync(
            managedIdentity,
            $"{ManagedIdentityEndpoint.Path}?resource=second&api-version=2019-08-01",
            "Secret",
            secret,
            timeout.Token);
        StreamingMessage secondRequest = await runtime.ReadAsync(timeout.Token);

        await WriteTokenResponseAsync(runtime, secondRequest, "second-token", timeout.Token);
        await WriteTokenResponseAsync(runtime, firstRequest, "first-token", timeout.Token);

        using HttpResponseMessage firstResponse = await firstTask;
        using HttpResponseMessage secondResponse = await secondTask;
        Assert.Equal("first-token", await ReadAccessTokenAsync(firstResponse, timeout.Token));
        Assert.Equal("second-token", await ReadAccessTokenAsync(secondResponse, timeout.Token));
    }

    private static Task<HttpResponseMessage> SendTokenRequestAsync(
        HttpClient client,
        string headerName,
        string secret,
        CancellationToken cancellationToken)
        => SendTokenRequestAsync(client, TokenRequestPath, headerName, secret, cancellationToken);

    private static async Task<HttpResponseMessage> SendTokenRequestAsync(
        HttpClient client,
        string requestPath,
        string headerName,
        string secret,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, requestPath);
        request.Headers.TryAddWithoutValidation(headerName, secret);
        return await client.SendAsync(request, cancellationToken);
    }

    private static async Task AssignAsync(
        HttpClient client,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
        string assignmentBody = JsonSerializer.Serialize(new
        {
            startupMode = "Preconfigured",
            functionAppName = "test-app",
            functionGroupName = "test-group",
            isAlwaysReady = false,
            environment,
            functionAppDirectory = string.Empty
        });
        using HttpResponseMessage assignment = await PutManagementJsonAsync(
            client,
            "/admin/worker/assignment",
            assignmentBody,
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, assignment.StatusCode);
    }

    private static async Task WriteTokenResponseAsync(
        RelayClient runtime,
        StreamingMessage request,
        string accessToken,
        CancellationToken cancellationToken)
    {
        await runtime.WriteAsync(new()
        {
            RequestId = request.RequestId,
            ManagedIdentityTokenResponse = new()
            {
                AccessToken = accessToken,
                TokenType = "Bearer",
                ExpiresOn = "4102444800",
                Resource = request.ManagedIdentityTokenRequest.Resource,
                ClientId = request.ManagedIdentityTokenRequest.ClientId,
                Result = new() { Status = StatusResult.Types.Status.Success }
            }
        }, cancellationToken);
    }

    private static async Task<string?> ReadAccessTokenAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return body.RootElement.GetProperty("access_token").GetString();
    }
}
