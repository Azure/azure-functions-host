// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Tests;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public class RpcClientFactoryTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ConcurrentCreateReusesChannelForEndpoint()
    {
        await using TestFunctionRpcServer server = await TestFunctionRpcServer.StartAsync();
        await using RpcClientFactory factory = CreateFactory();

        FunctionRpc.FunctionRpcClient[] clients = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => factory.CreateAsync(server.Endpoint).AsTask()));

        Assert.Equal(10, clients.Distinct().Count());
        Assert.Equal(1, factory.CachedChannelCount);
    }

    [Fact]
    public async Task CreateCachesDifferentChannelsForDifferentEndpoints()
    {
        await using TestFunctionRpcServer firstServer = await TestFunctionRpcServer.StartAsync();
        await using TestFunctionRpcServer secondServer = await TestFunctionRpcServer.StartAsync();
        await using RpcClientFactory factory = CreateFactory();

        await factory.CreateAsync(firstServer.Endpoint);
        await factory.CreateAsync(secondServer.Endpoint);

        Assert.Equal(2, factory.CachedChannelCount);
    }

    [Fact]
    public async Task FailedConnectionIsRemovedFromCache()
    {
        int unusedPort = GetUnusedPort();
        Uri endpoint = new($"https://{IPAddress.Loopback}:{unusedPort}");
        await using RpcClientFactory factory = CreateFactory();
        using CancellationTokenSource timeoutSource = new(TimeSpan.FromSeconds(1));

        await Assert.ThrowsAnyAsync<Exception>(() => factory.CreateAsync(endpoint, timeoutSource.Token).AsTask());

        Assert.Equal(0, factory.CachedChannelCount);
    }

    [Fact]
    public async Task FailedConnectionCanBeRetriedForSameEndpoint()
    {
        int attemptCount = 0;
        Task ConnectAsync(GrpcChannel channel, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Interlocked.Increment(ref attemptCount) == 1
                ? Task.FromException(new InvalidOperationException("connect failed"))
                : Task.CompletedTask;
        }

        await using RpcClientFactory factory = CreateFactory(ConnectAsync);
        Uri endpoint = new("https://localhost:5001");

        await Assert.ThrowsAsync<InvalidOperationException>(() => factory.CreateAsync(endpoint).AsTask());
        FunctionRpc.FunctionRpcClient client = await factory.CreateAsync(endpoint);

        Assert.NotNull(client);
        Assert.Equal(2, attemptCount);
        Assert.Equal(1, factory.CachedChannelCount);
    }

    [Fact]
    public async Task PreCanceledConnectionDoesNotCreateCacheEntry()
    {
        await using TestFunctionRpcServer server = await TestFunctionRpcServer.StartAsync();
        await using RpcClientFactory factory = CreateFactory();
        using CancellationTokenSource cancellationSource = new();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => factory.CreateAsync(server.Endpoint, cancellationSource.Token).AsTask());

        Assert.Equal(0, factory.CachedChannelCount);
    }

    [Fact]
    public async Task CancelingFirstConnectionDoesNotCancelWaitingConnection()
    {
        TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource secondStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int attemptCount = 0;
        async Task ConnectAsync(GrpcChannel channel, CancellationToken cancellationToken)
        {
            int attempt = Interlocked.Increment(ref attemptCount);
            (attempt == 1 ? firstStarted : secondStarted).TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        await using RpcClientFactory factory = CreateFactory(ConnectAsync);
        Uri endpoint = new("https://localhost:5001");
        using CancellationTokenSource firstSource = new();
        using CancellationTokenSource secondSource = new();
        Task<FunctionRpc.FunctionRpcClient> first = factory.CreateAsync(endpoint, firstSource.Token).AsTask();
        await firstStarted.Task.WaitAsync(TestTimeout);
        Task<FunctionRpc.FunctionRpcClient> second = factory.CreateAsync(endpoint, secondSource.Token).AsTask();

        firstSource.Cancel();
        OperationCanceledException firstException = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await secondStarted.Task.WaitAsync(TestTimeout);

        Assert.False(second.IsCompleted);
        Assert.Equal(firstSource.Token, firstException.CancellationToken);
        secondSource.Cancel();
        OperationCanceledException secondException = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.Equal(secondSource.Token, secondException.CancellationToken);
        Assert.Equal(0, factory.CachedChannelCount);
    }

    [Fact]
    public async Task CanceledCacheAccessPreservesConnectedChannel()
    {
        await using TestFunctionRpcServer server = await TestFunctionRpcServer.StartAsync();
        await using RpcClientFactory factory = CreateFactory();
        await factory.CreateAsync(server.Endpoint);
        using CancellationTokenSource cancellationSource = new();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => factory.CreateAsync(server.Endpoint, cancellationSource.Token).AsTask());

        Assert.Equal(1, factory.CachedChannelCount);
    }

    [Fact]
    public async Task DisposeCancelsAndWaitsForConnectionCreation()
    {
        TaskCompletionSource connectionStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource connectionStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task ConnectAsync(GrpcChannel channel, CancellationToken cancellationToken)
        {
            connectionStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            finally
            {
                connectionStopped.TrySetResult();
            }
        }

        RpcClientFactory factory = CreateFactory(ConnectAsync);
        Task<FunctionRpc.FunctionRpcClient> creation = factory.CreateAsync(new Uri("https://localhost:5001")).AsTask();
        await connectionStarted.Task.WaitAsync(TestTimeout);

        await factory.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => creation);
        Assert.True(connectionStopped.Task.IsCompletedSuccessfully);
        Assert.Equal(0, factory.CachedChannelCount);
    }

    [Fact]
    public async Task DisposeClearsCacheAndPreventsCreation()
    {
        await using TestFunctionRpcServer server = await TestFunctionRpcServer.StartAsync();
        RpcClientFactory factory = CreateFactory();
        await factory.CreateAsync(server.Endpoint);

        await factory.DisposeAsync();

        Assert.Equal(0, factory.CachedChannelCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => factory.CreateAsync(server.Endpoint).AsTask());
    }

    [Fact]
    public void HttpHandlerUsesTransportLivenessDefaults()
    {
        using SocketsHttpHandler handler = RpcClientFactory.CreateHttpHandler();

        Assert.Equal(TimeSpan.FromSeconds(5), handler.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), handler.KeepAlivePingDelay);
        Assert.Equal(TimeSpan.FromSeconds(10), handler.KeepAlivePingTimeout);
        Assert.Equal(HttpKeepAlivePingPolicy.Always, handler.KeepAlivePingPolicy);
    }

    [Fact]
    public void ValidateEndpointAcceptsHttpAuthority()
    {
        RpcClientFactory.ValidateEndpoint(new Uri("https://localhost:5001"));
    }

    [Fact]
    public void ValidateEndpointRejectsRelativeUri()
    {
        Assert.Throws<ArgumentException>(() =>
            RpcClientFactory.ValidateEndpoint(new Uri("relative", UriKind.Relative)));
    }

    [Theory]
    [InlineData("ftp://localhost")]
    [InlineData("file:///tmp/rpc.sock")]
    public void ValidateEndpointRejectsUnsupportedScheme(string endpoint)
    {
        Assert.Throws<ArgumentException>(() => RpcClientFactory.ValidateEndpoint(new Uri(endpoint)));
    }

    [Theory]
    [InlineData("https://localhost/functions")]
    [InlineData("https://localhost?query=value")]
    [InlineData("https://localhost#fragment")]
    public void ValidateEndpointRejectsCallSpecificComponents(string endpoint)
    {
        Assert.Throws<ArgumentException>(() => RpcClientFactory.ValidateEndpoint(new Uri(endpoint)));
    }

    [Fact]
    public void ValidateEndpointRejectsUserInformation()
    {
        Uri endpoint = new UriBuilder(Uri.UriSchemeHttp, "localhost")
        {
            UserName = "user",
        }.Uri;

        Assert.Throws<ArgumentException>(() => RpcClientFactory.ValidateEndpoint(endpoint));
    }

    [Fact]
    public void ExpiryOnlyPolicy_RejectsAbsentAndExpiryBoundaryCertificates()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using TestCertificateFiles certificates = new(now.AddDays(1));
        X509Certificate2 certificate = certificates.Certificate;
        DateTimeOffset end = certificate.NotAfter.ToUniversalTime();

        Assert.False(RpcClientFactory.IsCertificateUnexpired(null, now));
        Assert.True(RpcClientFactory.IsCertificateUnexpired(certificate, end.AddTicks(-1)));
        Assert.False(RpcClientFactory.IsCertificateUnexpired(certificate, end));
        Assert.False(RpcClientFactory.IsCertificateUnexpired(certificate, end.AddSeconds(1)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HttpClient_UsesExpiryOnlyPolicyOnRealTls(bool expired)
    {
        await using TestFunctionRpcServer server = await TestFunctionRpcServer.StartAsync(
            expires: DateTimeOffset.UtcNow.AddMinutes(expired ? -1 : 10));
        using HttpClient client = new(RpcClientFactory.CreateHttpHandler());
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(server.Endpoint, "/probe"))
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        if (expired)
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(request));
        }
        else
        {
            using HttpResponseMessage response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GrpcClient_UsesTheSameExpiryOnlyPolicyOnRealTls(bool expired)
    {
        await using TestFunctionRpcServer server = await TestFunctionRpcServer.StartAsync(
            expires: DateTimeOffset.UtcNow.AddMinutes(expired ? -1 : 10));
        await using RpcClientFactory factory = new(NullLogger<RpcClientFactory>.Instance);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
        FunctionRpc.FunctionRpcClient client = await factory.CreateAsync(server.Endpoint, timeout.Token);
        using var call = client.EventStream(cancellationToken: timeout.Token);
        if (expired)
        {
            Grpc.Core.RpcException failure = await Assert.ThrowsAsync<Grpc.Core.RpcException>(
                () => call.ResponseStream.MoveNext(timeout.Token));
            Assert.Contains(failure.StatusCode, new[] { StatusCode.Unavailable, StatusCode.Internal });
            Assert.Contains("SSL", failure.Status.Detail, StringComparison.OrdinalIgnoreCase);
            Assert.False(server.Service.Connected.IsCompleted);
        }
        else
        {
            await server.Service.Connected.WaitAsync(timeout.Token);
        }
    }

    private static RpcClientFactory CreateFactory()
        => new(NullLogger<RpcClientFactory>.Instance);

    private static RpcClientFactory CreateFactory(Func<GrpcChannel, CancellationToken, Task> connectAsync)
        => new(NullLogger<RpcClientFactory>.Instance, connectAsync);

    private static int GetUnusedPort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }
}
