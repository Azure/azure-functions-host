// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public class RpcClientConnectionIntegrationTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);
    private static readonly ILogger<RpcClientConnection> Logger = NullLogger<RpcClientConnection>.Instance;

    [Fact]
    public async Task ConnectionExchangesArbitraryMessagesInBothDirections()
    {
        await using TestFunctionRpcServer server = await TestFunctionRpcServer.StartAsync();
        RpcClientConnectionOptions options = new(server.Endpoint, "worker-1");
        await using RpcClientConnection connection = await CreateConnectionFactory().ConnectAsync(options);
        await server.Service.Connected.WaitAsync(TestTimeout);

        StreamingMessage outbound = new() { RequestId = "outbound" };
        await connection.EnqueueAsync(outbound);
        StreamingMessage receivedRequest = await server.Service.Requests.ReadAsync().AsTask().WaitAsync(TestTimeout);

        StreamingMessage inbound = new() { RequestId = "inbound" };
        await server.Service.SendResponseAsync(inbound);
        StreamingMessage receivedResponse = await ReadNextAsync(connection);

        Assert.Equal("outbound", receivedRequest.RequestId);
        Assert.Equal(StreamingMessage.ContentOneofCase.None, receivedRequest.ContentCase);
        Assert.Equal("inbound", receivedResponse.RequestId);
    }

    [Fact]
    public async Task PeerCloseCompletesConnectionCleanly()
    {
        await using TestFunctionRpcServer server = await TestFunctionRpcServer.StartAsync();
        RpcClientConnectionOptions options = new(server.Endpoint, "worker-1");
        await using RpcClientConnection connection = await CreateConnectionFactory().ConnectAsync(options);
        await server.Service.Connected.WaitAsync(TestTimeout);

        server.Service.CompleteResponses();

        await connection.Completion.WaitAsync(TestTimeout);
        Assert.Empty(await ReadAllAsync(connection));
    }

    [Fact]
    public async Task ReadAllAsyncEnumeratesMessagesUntilPeerClose()
    {
        await using TestFunctionRpcServer server = await TestFunctionRpcServer.StartAsync();
        RpcClientConnectionOptions options = new(server.Endpoint, "worker-1");
        await using RpcClientConnection connection = await CreateConnectionFactory().ConnectAsync(options);
        await server.Service.Connected.WaitAsync(TestTimeout);

        await server.Service.SendResponseAsync(new StreamingMessage { RequestId = "one" });
        await server.Service.SendResponseAsync(new StreamingMessage { RequestId = "two" });
        await server.Service.SendResponseAsync(new StreamingMessage { RequestId = "three" });
        server.Service.CompleteResponses();

        IReadOnlyList<StreamingMessage> messages = await ReadAllAsync(connection);

        Assert.Equal(["one", "two", "three"], messages.Select(message => message.RequestId));
        await connection.Completion.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task PeerFailureRemainsObservable()
    {
        await using TestFunctionRpcServer server = await TestFunctionRpcServer.StartAsync();
        RpcClientConnectionOptions options = new(server.Endpoint, "worker-1");
        RpcClientConnection connection = await CreateConnectionFactory().ConnectAsync(options);
        await server.Service.Connected.WaitAsync(TestTimeout);
        server.Service.CompleteResponses(new InvalidOperationException("server failure"));

        await Assert.ThrowsAsync<Grpc.Core.RpcException>(() => connection.Completion.WaitAsync(TestTimeout));
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task ConnectionFailureDoesNotReturnPartialConnection()
    {
        int unusedPort = GetUnusedPort();
        RpcClientConnectionOptions options = new(new Uri($"http://{IPAddress.Loopback}:{unusedPort}"), "worker-1");
        using CancellationTokenSource timeoutSource = new(TimeSpan.FromSeconds(1));

        await Assert.ThrowsAnyAsync<Exception>(() => CreateConnectionFactory().ConnectAsync(options, timeoutSource.Token));
    }

    [Fact]
    public async Task CanceledConnectionDoesNotReturnPartialConnection()
    {
        await using TestFunctionRpcServer server = await TestFunctionRpcServer.StartAsync();
        RpcClientConnectionOptions options = new(server.Endpoint, "worker-1");
        using CancellationTokenSource cancellationSource = new();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateConnectionFactory().ConnectAsync(options, cancellationSource.Token));
    }

    [Fact]
    public async Task ReachableServerRejectionSurfacesThroughCompletion()
    {
        await using TestFunctionRpcServer server = await TestFunctionRpcServer.StartAsync(mapService: false);
        RpcClientConnectionOptions options = new(server.Endpoint, "worker-1");
        RpcClientConnection connection = await CreateConnectionFactory().ConnectAsync(options);

        await Assert.ThrowsAsync<Grpc.Core.RpcException>(() => connection.Completion.WaitAsync(TestTimeout));
        await connection.DisposeAsync();
    }

    private static int GetUnusedPort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static IRpcClientConnectionFactory CreateConnectionFactory()
    {
        FunctionRpcDuplexChannelFactory channelFactory = new(NullLoggerFactory.Instance);
        return new RpcClientConnectionFactory(channelFactory, Logger);
    }

    private static async Task<IReadOnlyList<StreamingMessage>> ReadAllAsync(RpcClientConnection connection)
    {
        List<StreamingMessage> messages = [];
        using CancellationTokenSource timeoutSource = new(TestTimeout);
        await foreach (StreamingMessage message in connection.ReadAllAsync(timeoutSource.Token))
        {
            messages.Add(message);
        }

        return messages;
    }

    private static async Task<StreamingMessage> ReadNextAsync(RpcClientConnection connection)
    {
        using CancellationTokenSource timeoutSource = new(TestTimeout);
        await using IAsyncEnumerator<StreamingMessage> responses = connection.ReadAllAsync().GetAsyncEnumerator(timeoutSource.Token);
        Assert.True(await responses.MoveNextAsync());
        return responses.Current;
    }
}
