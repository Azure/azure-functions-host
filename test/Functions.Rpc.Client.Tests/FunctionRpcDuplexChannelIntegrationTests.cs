// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public class FunctionRpcDuplexChannelIntegrationTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ChannelExchangesArbitraryMessagesInBothDirections()
    {
        await using TestFunctionRpcServer server = await TestFunctionRpcServer.StartAsync();
        await using RpcClientFactory clientFactory = CreateClientFactory();
        FunctionRpcDuplexChannelFactory channelFactory = CreateChannelFactory(clientFactory);
        Channel<StreamingMessage> channel = await channelFactory.ConnectAsync(server.Endpoint);
        await using IAsyncDisposable channelLifetime = Assert.IsAssignableFrom<IAsyncDisposable>(channel);
        await server.Service.Connected.WaitAsync(TestTimeout);

        StreamingMessage outbound = new() { RequestId = "outbound" };
        await channel.Writer.WriteAsync(outbound);
        StreamingMessage receivedRequest = await server.Service.Requests.ReadAsync().AsTask().WaitAsync(TestTimeout);

        StreamingMessage inbound = new() { RequestId = "inbound" };
        await server.Service.SendResponseAsync(inbound);
        StreamingMessage receivedResponse = await ReadNextAsync(channel);

        Assert.Equal("outbound", receivedRequest.RequestId);
        Assert.Equal(StreamingMessage.ContentOneofCase.None, receivedRequest.ContentCase);
        Assert.Equal("inbound", receivedResponse.RequestId);
    }

    [Fact]
    public async Task PeerCloseCompletesChannelCleanly()
    {
        await using TestFunctionRpcServer server = await TestFunctionRpcServer.StartAsync();
        await using RpcClientFactory clientFactory = CreateClientFactory();
        FunctionRpcDuplexChannelFactory channelFactory = CreateChannelFactory(clientFactory);
        Channel<StreamingMessage> channel = await channelFactory.ConnectAsync(server.Endpoint);
        await using IAsyncDisposable channelLifetime = Assert.IsAssignableFrom<IAsyncDisposable>(channel);
        await server.Service.Connected.WaitAsync(TestTimeout);

        server.Service.CompleteResponses();

        Assert.Empty(await ReadAllAsync(channel));
        await channel.Reader.Completion.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task ReaderEnumeratesMessagesUntilPeerClose()
    {
        await using TestFunctionRpcServer server = await TestFunctionRpcServer.StartAsync();
        await using RpcClientFactory clientFactory = CreateClientFactory();
        FunctionRpcDuplexChannelFactory channelFactory = CreateChannelFactory(clientFactory);
        Channel<StreamingMessage> channel = await channelFactory.ConnectAsync(server.Endpoint);
        await using IAsyncDisposable channelLifetime = Assert.IsAssignableFrom<IAsyncDisposable>(channel);
        await server.Service.Connected.WaitAsync(TestTimeout);

        await server.Service.SendResponseAsync(new StreamingMessage { RequestId = "one" });
        await server.Service.SendResponseAsync(new StreamingMessage { RequestId = "two" });
        await server.Service.SendResponseAsync(new StreamingMessage { RequestId = "three" });
        server.Service.CompleteResponses();

        IReadOnlyList<StreamingMessage> messages = await ReadAllAsync(channel);

        Assert.Equal(["one", "two", "three"], messages.Select(message => message.RequestId));
        await channel.Reader.Completion.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task PeerFailureRemainsObservable()
    {
        await using TestFunctionRpcServer server = await TestFunctionRpcServer.StartAsync();
        await using RpcClientFactory clientFactory = CreateClientFactory();
        FunctionRpcDuplexChannelFactory channelFactory = CreateChannelFactory(clientFactory);
        Channel<StreamingMessage> channel = await channelFactory.ConnectAsync(server.Endpoint);
        await using IAsyncDisposable channelLifetime = Assert.IsAssignableFrom<IAsyncDisposable>(channel);
        await server.Service.Connected.WaitAsync(TestTimeout);
        server.Service.CompleteResponses(new InvalidOperationException("server failure"));

        await Assert.ThrowsAsync<Grpc.Core.RpcException>(() => channel.Reader.Completion.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task ConnectionFailureDoesNotReturnPartialStream()
    {
        int unusedPort = GetUnusedPort();
        Uri endpoint = new($"http://{IPAddress.Loopback}:{unusedPort}");
        await using RpcClientFactory clientFactory = CreateClientFactory();
        FunctionRpcDuplexChannelFactory channelFactory = CreateChannelFactory(clientFactory);
        using CancellationTokenSource timeoutSource = new(TimeSpan.FromSeconds(1));

        await Assert.ThrowsAnyAsync<Exception>(() => channelFactory.ConnectAsync(endpoint, timeoutSource.Token));
    }

    [Fact]
    public async Task CanceledConnectionDoesNotReturnPartialStream()
    {
        await using TestFunctionRpcServer server = await TestFunctionRpcServer.StartAsync();
        await using RpcClientFactory clientFactory = CreateClientFactory();
        FunctionRpcDuplexChannelFactory channelFactory = CreateChannelFactory(clientFactory);
        using CancellationTokenSource cancellationSource = new();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => channelFactory.ConnectAsync(server.Endpoint, cancellationSource.Token));
    }

    [Fact]
    public async Task DisposeCompletesReaderCleanly()
    {
        await using TestFunctionRpcServer server = await TestFunctionRpcServer.StartAsync();
        await using RpcClientFactory clientFactory = CreateClientFactory();
        FunctionRpcDuplexChannelFactory channelFactory = CreateChannelFactory(clientFactory);
        Channel<StreamingMessage> channel = await channelFactory.ConnectAsync(server.Endpoint);
        IAsyncDisposable channelLifetime = Assert.IsAssignableFrom<IAsyncDisposable>(channel);
        await server.Service.Connected.WaitAsync(TestTimeout);

        await channelLifetime.DisposeAsync();

        await channel.Reader.Completion.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task ReachableServerRejectionFaultsChannel()
    {
        await using TestFunctionRpcServer server = await TestFunctionRpcServer.StartAsync(mapService: false);
        await using RpcClientFactory clientFactory = CreateClientFactory();
        FunctionRpcDuplexChannelFactory channelFactory = CreateChannelFactory(clientFactory);
        Channel<StreamingMessage> channel = await channelFactory.ConnectAsync(server.Endpoint);
        await using IAsyncDisposable channelLifetime = Assert.IsAssignableFrom<IAsyncDisposable>(channel);

        await Assert.ThrowsAsync<Grpc.Core.RpcException>(() => channel.Reader.Completion.WaitAsync(TestTimeout));
    }

    private static RpcClientFactory CreateClientFactory()
        => new(NullLogger<RpcClientFactory>.Instance);

    private static FunctionRpcDuplexChannelFactory CreateChannelFactory(IRpcClientFactory clientFactory)
        => new(clientFactory, NullLogger<FunctionRpcDuplexChannelFactory>.Instance);

    private static int GetUnusedPort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }

    private static async Task<IReadOnlyList<StreamingMessage>> ReadAllAsync(Channel<StreamingMessage> channel)
    {
        List<StreamingMessage> messages = [];
        using CancellationTokenSource timeoutSource = new(TestTimeout);
        await foreach (StreamingMessage message in channel.Reader.ReadAllAsync(timeoutSource.Token))
        {
            messages.Add(message);
        }

        return messages;
    }

    private static async Task<StreamingMessage> ReadNextAsync(Channel<StreamingMessage> channel)
    {
        using CancellationTokenSource timeoutSource = new(TestTimeout);
        return await channel.Reader.ReadAsync(timeoutSource.Token);
    }
}
