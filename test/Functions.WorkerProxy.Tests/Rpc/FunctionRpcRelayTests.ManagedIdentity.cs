// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
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
        $"{ManagedIdentityEndpoint.Path}?resource=https%3A%2F%2Fstorage.azure.com%2F&api-version=2019-08-01&client_id=client-id";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ManagedIdentityMessages_FromWorkerTerminateRelay(bool request)
    {
        await using FunctionRpcRelay relay = CreateInProcessRelay();
        using CancellationTokenSource timeout = new(TestTimeout);
        Channel<StreamingMessage> workerInbound = Channel.CreateUnbounded<StreamingMessage>();
        Task<FunctionRpcRelayTerminalState> runtimeTask = relay.AttachAsync(
            FunctionRpcRelaySide.Runtime,
            new BlockingStreamReader(),
            new TestServerStreamWriter(),
            timeout.Token);
        Task<FunctionRpcRelayTerminalState> workerTask = relay.AttachAsync(
            FunctionRpcRelaySide.Worker,
            CreateMessageReader(workerInbound.Reader),
            new TestServerStreamWriter(),
            timeout.Token);
        await workerInbound.Writer.WriteAsync(CreateStartStream(), timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Worker, timeout.Token);

        StreamingMessage reserved = request
            ? new() { ManagedIdentityTokenRequest = new() }
            : new() { ManagedIdentityTokenResponse = new() };
        await workerInbound.Writer.WriteAsync(reserved, timeout.Token);

        FunctionRpcRelayTerminalState[] terminalStates =
            await Task.WhenAll(runtimeTask, workerTask).WaitAsync(timeout.Token);
        Assert.All(terminalStates, state =>
        {
            Assert.Equal(FunctionRpcRelayTerminationReason.Faulted, state.Reason);
            Assert.Equal(FunctionRpcRelaySide.Worker, state.Side);
            Assert.IsType<InvalidDataException>(state.Exception);
        });
    }

    [Theory]
    [InlineData("MSI_SECRET", "Secret", "2017-09-01", "clientid")]
    [InlineData("IDENTITY_HEADER", "X-IDENTITY-HEADER", "2019-08-01", "client_id")]
    public async Task ManagedIdentityEndpoint_UsesAssignedSecretAndExistingRuntimeStream(
        string environmentName,
        string headerName,
        string apiVersion,
        string clientIdQuery)
    {
        const string secret = "worker-local-secret";
        const string tokenBody =
            """{"access_token":"real-token","token_type":"Bearer","expires_on":"4102444800","resource":"https://storage.azure.com/","client_id":"client-id"}""";
        string tokenRequestPath =
            $"{ManagedIdentityEndpoint.Path}?resource=https%3A%2F%2Fstorage.azure.com%2F&api-version={apiVersion}&{clientIdQuery}=client-id";
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using HttpClient managedIdentity = factory.CreateManagedIdentityClient();
        using CancellationTokenSource timeout = new(TestTimeout);

        using (HttpResponseMessage unassigned = await SendTokenRequestAsync(
            managedIdentity, tokenRequestPath, headerName, secret, timeout.Token))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unassigned.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(1), unassigned.Headers.RetryAfter?.Delta);
        }

        await AssignAsync(management, new Dictionary<string, string> { [environmentName] = secret }, timeout.Token);

        Assert.Equal(WorkerAssignmentState.Unassigned, manager.State.AssignmentState);
        Assert.False(manager.State.IsWorkerReady);

        using (HttpResponseMessage forbidden = await SendTokenRequestAsync(
            managedIdentity, tokenRequestPath, headerName, "wrong-secret", timeout.Token))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        using (HttpResponseMessage noRuntime = await SendTokenRequestAsync(
            managedIdentity, tokenRequestPath, headerName, secret, timeout.Token))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, noRuntime.StatusCode);
        }

        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);

        using (HttpResponseMessage invalidSelector = await SendTokenRequestAsync(
            managedIdentity,
            $"{ManagedIdentityEndpoint.Path}?resource=resource&api-version=2019-08-01&mi_res_id=identity",
            headerName,
            secret,
            timeout.Token))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalidSelector.StatusCode);
        }

        using (HttpResponseMessage ambiguousClientId = await SendTokenRequestAsync(
            managedIdentity,
            $"{ManagedIdentityEndpoint.Path}?resource=resource&api-version={apiVersion}&clientid=one&client_id=two",
            headerName,
            secret,
            timeout.Token))
        {
            Assert.Equal(HttpStatusCode.BadRequest, ambiguousClientId.StatusCode);
        }

        Task<HttpResponseMessage> tokenTask = SendTokenRequestAsync(
            managedIdentity, tokenRequestPath, headerName, secret, timeout.Token);
        StreamingMessage tokenRequest = await runtime.ReadAsync(timeout.Token);
        Assert.Equal(StreamingMessage.ContentOneofCase.ManagedIdentityTokenRequest, tokenRequest.ContentCase);
        Assert.Equal("https://storage.azure.com/", tokenRequest.ManagedIdentityTokenRequest.Resource);
        Assert.Equal("client-id", tokenRequest.ManagedIdentityTokenRequest.ClientId);
        Assert.Equal(apiVersion, tokenRequest.ManagedIdentityTokenRequest.ApiVersion);
        Assert.True(ManagedIdentityTokenRequestAuthenticator.IsValid(
            "runtime-access-key",
            tokenRequest.RequestId,
            tokenRequest.ManagedIdentityTokenRequest));

        await WriteTokenResponseAsync(
            runtime,
            tokenRequest,
            HttpStatusCode.OK,
            tokenBody,
            timeout.Token);

        using HttpResponseMessage token = await tokenTask;
        Assert.Equal(HttpStatusCode.OK, token.StatusCode);
        Assert.Equal("application/json", token.Content.Headers.ContentType?.MediaType);
        using JsonDocument body = JsonDocument.Parse(await token.Content.ReadAsStringAsync(timeout.Token));
        Assert.Equal("real-token", body.RootElement.GetProperty("access_token").GetString());
        Assert.Equal("Bearer", body.RootElement.GetProperty("token_type").GetString());
        Assert.Equal("4102444800", body.RootElement.GetProperty("expires_on").GetString());
        Assert.Equal("https://storage.azure.com/", body.RootElement.GetProperty("resource").GetString());
        Assert.Equal("client-id", body.RootElement.GetProperty("client_id").GetString());
        Assert.False(manager.State.IsWorkerReady);
        Assert.Equal(WorkerAssignmentState.Unassigned, manager.State.AssignmentState);

        const string errorBody =
            """{"error":"invalid_request","error_description":"The requested identity is not assigned."}""";
        Task<HttpResponseMessage> rejectedTask = SendTokenRequestAsync(
            managedIdentity,
            tokenRequestPath,
            headerName,
            secret,
            timeout.Token);
        StreamingMessage rejectedRequest = await runtime.ReadAsync(timeout.Token);
        await WriteTokenResponseAsync(
            runtime,
            rejectedRequest,
            HttpStatusCode.BadRequest,
            errorBody,
            timeout.Token);

        using HttpResponseMessage rejected = await rejectedTask;
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(errorBody, await rejected.Content.ReadAsStringAsync(timeout.Token));
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

        using CancellationTokenSource canceledRequestSource =
            CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        Task<HttpResponseMessage> canceledTask = SendTokenRequestAsync(
            managedIdentity,
            $"{ManagedIdentityEndpoint.Path}?resource=canceled&api-version=2019-08-01",
            "Secret",
            secret,
            canceledRequestSource.Token);
        StreamingMessage canceledRequest = await runtime.ReadAsync(timeout.Token);
        canceledRequestSource.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledTask);

        const int activeRequestCount = 15;
        Task<HttpResponseMessage>[] responseTasks = new Task<HttpResponseMessage>[activeRequestCount];
        StreamingMessage[] requests = new StreamingMessage[activeRequestCount];
        for (int i = 0; i < activeRequestCount; i++)
        {
            responseTasks[i] = SendTokenRequestAsync(
                managedIdentity,
                $"{ManagedIdentityEndpoint.Path}?resource=resource-{i}&api-version=2019-08-01",
                "Secret",
                secret,
                timeout.Token);
            requests[i] = await runtime.ReadAsync(timeout.Token);
        }

        using CancellationTokenSource overflowTimeout =
            CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        overflowTimeout.CancelAfter(TimeSpan.FromSeconds(1));
        using HttpResponseMessage overflow = await SendTokenRequestAsync(
            managedIdentity,
            $"{ManagedIdentityEndpoint.Path}?resource=overflow&api-version=2019-08-01",
            "Secret",
            secret,
            overflowTimeout.Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, overflow.StatusCode);

        await WriteTokenResponseAsync(
            runtime,
            canceledRequest,
            HttpStatusCode.OK,
            CreateTokenBody("canceled-token", "canceled"),
            timeout.Token);

        for (int i = activeRequestCount - 1; i >= 0; i--)
        {
            await WriteTokenResponseAsync(
                runtime,
                requests[i],
                HttpStatusCode.OK,
                CreateTokenBody($"token-{i}", $"resource-{i}"),
                timeout.Token);
        }

        for (int i = 0; i < activeRequestCount; i++)
        {
            using HttpResponseMessage response = await responseTasks[i];
            Assert.Equal($"token-{i}", await ReadAccessTokenAsync(response, timeout.Token));
        }
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
}
