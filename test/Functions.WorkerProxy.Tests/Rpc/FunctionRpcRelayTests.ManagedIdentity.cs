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

        string assignmentBody = JsonSerializer.Serialize(new
        {
            startupMode = "Preconfigured",
            functionAppName = "test-app",
            functionGroupName = "test-group",
            isAlwaysReady = false,
            environment = new Dictionary<string, string> { [environmentName] = secret },
            functionAppDirectory = string.Empty
        });
        using (HttpResponseMessage assignment = await PutManagementJsonAsync(
            management, "/admin/worker/assignment", assignmentBody, timeout.Token))
        {
            Assert.Equal(HttpStatusCode.Created, assignment.StatusCode);
        }

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

        await runtime.WriteAsync(new()
        {
            RequestId = tokenRequest.RequestId,
            ManagedIdentityTokenResponse = new()
            {
                AccessToken = "dummy-token",
                TokenType = "Bearer",
                ExpiresOn = "4102444800",
                Resource = tokenRequest.ManagedIdentityTokenRequest.Resource,
                ClientId = tokenRequest.ManagedIdentityTokenRequest.ClientId,
                Result = new() { Status = StatusResult.Types.Status.Success }
            }
        }, timeout.Token);

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

    private static async Task<HttpResponseMessage> SendTokenRequestAsync(
        HttpClient client,
        string headerName,
        string secret,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get,
            $"{ManagedIdentityEndpoint.Path}?resource=https%3A%2F%2Fstorage.azure.com%2F&api-version=2019-08-01&client_id=client-id");
        request.Headers.TryAddWithoutValidation(headerName, secret);
        return await client.SendAsync(request, cancellationToken);
    }
}
