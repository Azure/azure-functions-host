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

public class FunctionRpcDuplexStreamIntegrationTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task StreamExchangesArbitraryMessagesInBothDirections()
    {
        await using TestFunctionRpcServer server = await TestFunctionRpcServer.StartAsync();
        Channel<StreamingMessage> stream = await CreateFactory().ConnectAsync(server.Endpoint);
        await using IAsyncDisposable streamLifetime = Assert.IsAssignableFrom<IAsyncDisposable>(stream);
        await server.Service.Connected.WaitAsync(TestTimeout);

        StreamingMessage outbound = new() { RequestId = "outbound" };
        await stream.Writer.WriteAsync(outbound);
        StreamingMessage receivedRequest = await server.Service.Requests.ReadAsync().AsTask().WaitAsync(TestTimeout);

        StreamingMessage inbound = new() { RequestId = "inbound" };
        await server.Service.SendResponseAsync(inbound);
        StreamingMessage receivedResponse = await ReadNextAsync(stream);

        Assert.Equal("outbound", receivedRequest.RequestId);
        Assert.Equal(StreamingMessage.ContentOneofCase.None, receivedRequest.ContentCase);
        Assert.Equal("inbound", receivedResponse.RequestId);
    }

    [Fact]
    public async Task PeerCloseCompletesStreamCleanly()
    {
        await using TestFunctionRpcServer server = await TestFunctionRpcServer.StartAsync();
        Channel<StreamingMessage> stream = await CreateFactory().ConnectAsync(server.Endpoint);
        await using IAsyncDisposable streamLifetime = Assert.IsAssignableFrom<IAsyncDisposable>(stream);
        await server.Service.Connected.WaitAsync(TestTimeout);

        server.Service.CompleteResponses();

        Assert.Empty(await ReadAllAsync(stream));
        await stream.Reader.Completion.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task ReaderEnumeratesMessagesUntilPeerClose()
    {
        await using TestFunctionRpcServer server = await TestFunctionRpcServer.StartAsync();
        Channel<StreamingMessage> stream = await CreateFactory().ConnectAsync(server.Endpoint);
        await using IAsyncDisposable streamLifetime = Assert.IsAssignableFrom<IAsyncDisposable>(stream);
        await server.Service.Connected.WaitAsync(TestTimeout);

        await server.Service.SendResponseAsync(new StreamingMessage { RequestId = "one" });
        await server.Service.SendResponseAsync(new StreamingMessage { RequestId = "two" });
        await server.Service.SendResponseAsync(new StreamingMessage { RequestId = "three" });
        server.Service.CompleteResponses();

        IReadOnlyList<StreamingMessage> messages = await ReadAllAsync(stream);

        Assert.Equal(["one", "two", "three"], messages.Select(message => message.RequestId));
        await stream.Reader.Completion.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task PeerFailureRemainsObservable()
    {
        await using TestFunctionRpcServer server = await TestFunctionRpcServer.StartAsync();
        Channel<StreamingMessage> stream = await CreateFactory().ConnectAsync(server.Endpoint);
        await using IAsyncDisposable streamLifetime = Assert.IsAssignableFrom<IAsyncDisposable>(stream);
        await server.Service.Connected.WaitAsync(TestTimeout);
        server.Service.CompleteResponses(new InvalidOperationException("server failure"));

        await Assert.ThrowsAsync<Grpc.Core.RpcException>(() => stream.Reader.Completion.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task ConnectionFailureDoesNotReturnPartialStream()
    {
        int unusedPort = GetUnusedPort();
        Uri endpoint = new($"http://{IPAddress.Loopback}:{unusedPort}");
        using CancellationTokenSource timeoutSource = new(TimeSpan.FromSeconds(1));

        await Assert.ThrowsAnyAsync<Exception>(() =>
            CreateFactory().ConnectAsync(endpoint, timeoutSource.Token));
    }

    [Fact]
    public async Task CanceledConnectionDoesNotReturnPartialStream()
    {
        await using TestFunctionRpcServer server = await TestFunctionRpcServer.StartAsync();
        using CancellationTokenSource cancellationSource = new();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateFactory().ConnectAsync(server.Endpoint, cancellationSource.Token));
    }

    [Fact]
    public async Task DisposeCompletesReaderCleanly()
    {
        await using TestFunctionRpcServer server = await TestFunctionRpcServer.StartAsync();
        Channel<StreamingMessage> stream = await CreateFactory().ConnectAsync(server.Endpoint);
        IAsyncDisposable streamLifetime = Assert.IsAssignableFrom<IAsyncDisposable>(stream);
        await server.Service.Connected.WaitAsync(TestTimeout);

        await streamLifetime.DisposeAsync();

        await stream.Reader.Completion.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task ReachableServerRejectionFaultsStream()
    {
        await using TestFunctionRpcServer server = await TestFunctionRpcServer.StartAsync(mapService: false);
        Channel<StreamingMessage> stream = await CreateFactory().ConnectAsync(server.Endpoint);
        await using IAsyncDisposable streamLifetime = Assert.IsAssignableFrom<IAsyncDisposable>(stream);

        await Assert.ThrowsAsync<Grpc.Core.RpcException>(() => stream.Reader.Completion.WaitAsync(TestTimeout));
    }

    private static FunctionRpcDuplexStreamFactory CreateFactory()
        => new(NullLogger<FunctionRpcDuplexStreamFactory>.Instance);

    private static int GetUnusedPort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }

    private static async Task<IReadOnlyList<StreamingMessage>> ReadAllAsync(Channel<StreamingMessage> stream)
    {
        List<StreamingMessage> messages = [];
        using CancellationTokenSource timeoutSource = new(TestTimeout);
        await foreach (StreamingMessage message in stream.Reader.ReadAllAsync(timeoutSource.Token))
        {
            messages.Add(message);
        }

        return messages;
    }

    private static async Task<StreamingMessage> ReadNextAsync(Channel<StreamingMessage> stream)
    {
        using CancellationTokenSource timeoutSource = new(TestTimeout);
        return await stream.Reader.ReadAsync(timeoutSource.Token);
    }
}
