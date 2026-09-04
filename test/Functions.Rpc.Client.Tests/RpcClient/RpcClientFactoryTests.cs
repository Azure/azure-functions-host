// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
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
        Uri endpoint = new($"http://{IPAddress.Loopback}:{unusedPort}");
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
        Uri endpoint = new("http://localhost:5001");

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
        Uri endpoint = new("http://localhost:5001");
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
        Task<FunctionRpc.FunctionRpcClient> creation = factory.CreateAsync(new Uri("http://localhost:5001")).AsTask();
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
    [InlineData("http://localhost/functions")]
    [InlineData("http://localhost?query=value")]
    [InlineData("http://localhost#fragment")]
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
