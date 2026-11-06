// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Authentication;
using Moq;
using Moq.Protected;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed class WorkerProxyHttpMessageHandlerTests
{
    [Fact]
    public async Task SendAsync_ReplacesReservedOutboundHeadersAndPreservesCustomerAuthorization()
    {
        Mock<HttpMessageHandler> inner = new();
        inner.Protected().Setup<Task<HttpResponseMessage>>(
            "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new(HttpStatusCode.OK));
        using HttpMessageInvoker client = new(new WorkerProxyHttpMessageHandler(RuntimeWorkerTestData.CreateCredentials()) { InnerHandler = inner.Object });
        using HttpRequestMessage request = CreateRequest();

        using HttpResponseMessage response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/invoke?customer=value", request.RequestUri.PathAndQuery);
        Assert.Equal("Bearer", request.Headers.Authorization.Scheme);
        Assert.Equal("customer-authorization", request.Headers.Authorization.Parameter);
        string token = Assert.Single(request.Headers.GetValues(RuntimeWorkerAuthentication.WorkerTokenHeader));
        Assert.True(RuntimeWorkerAuthentication.ValidateToken(token, Convert.FromBase64String(RuntimeWorkerTestData.WorkerKey),
            RuntimeWorkerAuthentication.WorkerAudience("worker"), RuntimeWorkerAuthentication.WorkerPurpose, DateTimeOffset.UtcNow));
        Assert.False(request.Headers.Contains(RuntimeWorkerAuthentication.AppTokenHeader));
        Assert.False(request.Headers.Contains(RuntimeWorkerAuthentication.SiteTokenHeader));
        Assert.All(RuntimeWorkerAuthentication.TokenHeaders, name => Assert.False(request.Content.Headers.Contains(name)));
        inner.Protected().Verify(
            "SendAsync", Times.Once(), ItExpr.Is<HttpRequestMessage>(value => ReferenceEquals(value, request)), ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_ConcurrentWorkersAndReplacementHaveIndependentKeysAndTransportOwnership()
    {
        byte[] replacementKey = SHA256.HashData("replacement-worker-key"u8);
        RuntimeWorkerCredentials[] credentials =
        [
            RuntimeWorkerTestData.CreateCredentials("worker-a"),
            RuntimeWorkerTestData.CreateCredentials("worker-b"),
            new("worker-a", Convert.ToHexString(replacementKey), TimeProvider.System)
        ];
        ConcurrentDictionary<int, string> tokens = new();
        Mock<HttpMessageHandler>[] handlers = [new(), new(), new()];
        HttpMessageInvoker[] clients = credentials.Select((value, index) =>
        {
            handlers[index].Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns(async (HttpRequestMessage request, CancellationToken _) =>
                {
                    await Task.Yield();
                    tokens[index] = Assert.Single(request.Headers.GetValues(RuntimeWorkerAuthentication.WorkerTokenHeader));

                    return new HttpResponseMessage(HttpStatusCode.OK);
                });

            return new HttpMessageInvoker(new WorkerProxyHttpMessageHandler(value) { InnerHandler = handlers[index].Object });
        }).ToArray();
        try
        {
            await Task.WhenAll(clients.Select(async client =>
            {
                using HttpRequestMessage request = CreateRequest();
                using HttpResponseMessage response = await client.SendAsync(request, CancellationToken.None);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }));
            for (int index = 0; index < credentials.Length; index++)
            {
                string workerId = index is 1 ? "worker-b" : "worker-a";
                byte[] key = index is 2 ? replacementKey : Convert.FromBase64String(RuntimeWorkerTestData.WorkerKey);
                Assert.True(RuntimeWorkerAuthentication.ValidateToken(tokens[index], key,
                    RuntimeWorkerAuthentication.WorkerAudience(workerId), RuntimeWorkerAuthentication.WorkerPurpose, DateTimeOffset.UtcNow));
                Assert.False(RuntimeWorkerAuthentication.ValidateToken(tokens[index],
                    index is 2 ? Convert.FromBase64String(RuntimeWorkerTestData.WorkerKey) : replacementKey,
                    RuntimeWorkerAuthentication.WorkerAudience(workerId), RuntimeWorkerAuthentication.WorkerPurpose, DateTimeOffset.UtcNow));
            }

            clients[0].Dispose();
            handlers[0].Protected().Verify("Dispose", Times.Once(), ItExpr.Is<bool>(value => value));
            handlers[1].Protected().Verify("Dispose", Times.Never(), ItExpr.IsAny<bool>());
            using HttpRequestMessage nextRequest = CreateRequest();
            using HttpResponseMessage nextResponse = await clients[1].SendAsync(nextRequest, CancellationToken.None);
            Assert.Equal(HttpStatusCode.OK, nextResponse.StatusCode);
        }
        finally
        {
            foreach (HttpMessageInvoker client in clients)
            {
                client.Dispose();
            }
        }
    }

    [Fact]
    public async Task SendAsync_RejectsPlaintextBeforeSendingCredentials()
    {
        Mock<HttpMessageHandler> inner = new(MockBehavior.Strict);
        inner.Protected().Setup("Dispose", ItExpr.IsAny<bool>());
        using HttpMessageInvoker client = new(new WorkerProxyHttpMessageHandler(RuntimeWorkerTestData.CreateCredentials()) { InnerHandler = inner.Object });
        using HttpRequestMessage request = new(HttpMethod.Get, "http://worker-proxy:28080/");

        await Assert.ThrowsAsync<ArgumentException>(() => client.SendAsync(request, CancellationToken.None));

        inner.Protected().Verify("SendAsync", Times.Never(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    private static HttpRequestMessage CreateRequest()
    {
        HttpRequestMessage request = new(HttpMethod.Get, "https://worker-proxy:28080/invoke?customer=value");
        request.Headers.Authorization = new("Bearer", "customer-authorization");
        request.Content = new StringContent("customer-request");
        foreach (string name in RuntimeWorkerAuthentication.TokenHeaders)
        {
            request.Headers.Add(name, "caller-supplied-private-header");
            request.Content.Headers.TryAddWithoutValidation(name, "caller-supplied-content-header");
        }

        return request;
    }
}
