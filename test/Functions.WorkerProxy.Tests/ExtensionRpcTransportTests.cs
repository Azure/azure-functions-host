// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Options;
using Xunit;
using GrpcRpcException = Grpc.Core.RpcException;

namespace Azure.Functions.WorkerProxy.Tests;

public class ExtensionRpcTransportTests
{
    [Fact]
    public async Task OpenExtensionCall_RoutesMessagesThroughSession()
    {
        var streamCoordinator = new ExtensionRpcStreamCoordinator();
        await using ExtensionRpcStreamLease lease = streamCoordinator.Open(CancellationToken.None);

        ExtensionRpcMessage hello = await lease.Stream.Outbound.ReadAsync();
        Assert.Contains(ExtensionRpcStreamCoordinator.ProtocolVersion, hello.Hello.SupportedVersions);
        await lease.Stream.HandleInboundAsync(CreateReady(hello), CancellationToken.None);

        await using ExtensionCall call = await streamCoordinator.OpenExtensionCallAsync(
            new ExtensionRpcStart { Method = "/extensions.Echo/Unary" },
            CancellationToken.None);

        ExtensionRpcMessage start = await lease.Stream.Outbound.ReadAsync();
        Assert.Equal(hello.SessionId, start.SessionId);
        Assert.Equal(hello.ShardId, start.ShardId);
        Assert.Equal(call.CallId, start.CallId);
        Assert.Equal("/extensions.Echo/Unary", start.Start.Method);

        await lease.Stream.HandleInboundAsync(
            new ExtensionRpcMessage
            {
                SessionId = hello.SessionId,
                ShardId = hello.ShardId,
                CallId = call.CallId,
                Headers = new ExtensionRpcHeaders(),
            },
            CancellationToken.None);

        ExtensionRpcMessage received = await FirstAsync(call.ReadAllAsync(CancellationToken.None));
        Assert.Equal(ExtensionRpcMessage.ContentOneofCase.Headers, received.ContentCase);
    }

    [Fact]
    public async Task Open_RejectsSecondConcurrentPhysicalStream()
    {
        var streamCoordinator = new ExtensionRpcStreamCoordinator();
        await using ExtensionRpcStreamLease lease = streamCoordinator.Open(CancellationToken.None);

        Assert.Throws<InvalidOperationException>(() => streamCoordinator.Open(CancellationToken.None));
    }

    [Fact]
    public async Task OpenExtensionCall_AllowsMoreThanPreviousCapacity()
    {
        var streamCoordinator = new ExtensionRpcStreamCoordinator();
        await using ExtensionRpcStreamLease lease = streamCoordinator.Open(CancellationToken.None);
        ExtensionRpcMessage hello = await lease.Stream.Outbound.ReadAsync();
        await lease.Stream.HandleInboundAsync(CreateReady(hello), CancellationToken.None);
        var calls = new List<ExtensionCall>();

        try
        {
            for (int i = 0; i < 129; i++)
            {
                calls.Add(await streamCoordinator.OpenExtensionCallAsync(
                    new ExtensionRpcStart { Method = "/extensions.Echo/Unary" },
                    CancellationToken.None));
                await lease.Stream.Outbound.ReadAsync();
            }

            Assert.Equal(129, lease.Stream.ActiveCallCount);
        }
        finally
        {
            foreach (ExtensionCall call in calls)
            {
                await call.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task OpenExtensionCall_PropagatesRemainingTimeout()
    {
        var streamCoordinator = new ExtensionRpcStreamCoordinator();
        await using ExtensionRpcStreamLease lease = streamCoordinator.Open(CancellationToken.None);
        ExtensionRpcMessage hello = await lease.Stream.Outbound.ReadAsync();
        await lease.Stream.HandleInboundAsync(CreateReady(hello), CancellationToken.None);
        var start = new ExtensionRpcStart
        {
            Method = "/extensions.Echo/Unary",
            Timeout = Duration.FromTimeSpan(TimeSpan.FromSeconds(1)),
        };
        start.Metadata.Add(
            new ExtensionRpcMetadataEntry
            {
                Key = "grpc-timeout",
                Value = ByteString.CopyFromUtf8("1S"),
            });

        await using ExtensionCall call =
            await streamCoordinator.OpenExtensionCallAsync(start, CancellationToken.None);
        ExtensionRpcMessage message = await lease.Stream.Outbound.ReadAsync();

        Assert.True(message.Start.Timeout.ToTimeSpan() < TimeSpan.FromSeconds(1));
        Assert.NotEqual(
            "1S",
            Assert.Single(
                message.Start.Metadata,
                entry => string.Equals(entry.Key, "grpc-timeout", StringComparison.OrdinalIgnoreCase))
                .Value.ToStringUtf8());
    }

    [Fact]
    public async Task SessionClosed_AllowsNewSession()
    {
        var streamCoordinator = new ExtensionRpcStreamCoordinator();
        string firstSessionId;
        string firstStreamId;
        await using (ExtensionRpcStreamLease firstLease = streamCoordinator.Open(CancellationToken.None))
        {
            ExtensionRpcMessage hello = await firstLease.Stream.Outbound.ReadAsync();
            firstSessionId = hello.SessionId;
            firstStreamId = hello.ShardId;
            await firstLease.Stream.HandleInboundAsync(
                new ExtensionRpcMessage
                {
                    SessionId = hello.SessionId,
                    ShardId = hello.ShardId,
                    SessionClosed = new ExtensionRpcSessionClosed(),
                },
                CancellationToken.None);
        }

        Assert.False(streamCoordinator.HasConnectedStream);

        await using ExtensionRpcStreamLease secondLease = streamCoordinator.Open(CancellationToken.None);
        ExtensionRpcMessage secondHello = await secondLease.Stream.Outbound.ReadAsync();
        Assert.NotEqual(firstSessionId, secondHello.SessionId);
        Assert.NotEqual(firstStreamId, secondHello.ShardId);
    }

    [Fact]
    public async Task TransportDisconnect_PreservesSessionForReconnect()
    {
        var streamCoordinator = new ExtensionRpcStreamCoordinator();
        string firstSessionId;
        string firstStreamId;
        await using (ExtensionRpcStreamLease firstLease = streamCoordinator.Open(CancellationToken.None))
        {
            ExtensionRpcMessage hello = await firstLease.Stream.Outbound.ReadAsync();
            firstSessionId = hello.SessionId;
            firstStreamId = hello.ShardId;
        }

        await using ExtensionRpcStreamLease secondLease = streamCoordinator.Open(CancellationToken.None);
        ExtensionRpcMessage secondHello = await secondLease.Stream.Outbound.ReadAsync();

        Assert.Equal(firstSessionId, secondHello.SessionId);
        Assert.NotEqual(firstStreamId, secondHello.ShardId);
    }

    [Fact]
    public async Task OpenExtensionCall_RejectsInvalidNegotiation()
    {
        var streamCoordinator = new ExtensionRpcStreamCoordinator();
        await using ExtensionRpcStreamLease lease = streamCoordinator.Open(CancellationToken.None);
        ExtensionRpcMessage hello = await lease.Stream.Outbound.ReadAsync();
        ExtensionRpcMessage ready = CreateReady(hello);
        ready.Ready.SelectedVersion++;

        await lease.Stream.HandleInboundAsync(ready, CancellationToken.None);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => streamCoordinator.OpenExtensionCallAsync(
                new ExtensionRpcStart { Method = "/extensions.Echo/Unary" },
                CancellationToken.None));
        Assert.Contains("disabled", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteAsync_WaitsForReceiveWindowCredit()
    {
        var streamCoordinator = new ExtensionRpcStreamCoordinator();
        await using ExtensionRpcStreamLease lease = streamCoordinator.Open(CancellationToken.None);
        ExtensionRpcMessage hello = await lease.Stream.Outbound.ReadAsync();
        ExtensionRpcMessage ready = CreateReady(hello);
        ready.Ready.InitialReceiveWindowBytes = 2;
        ready.Ready.MaxDataChunkBytes = 2;
        await lease.Stream.HandleInboundAsync(ready, CancellationToken.None);
        await using ExtensionCall call = await streamCoordinator.OpenExtensionCallAsync(
            new ExtensionRpcStart { Method = "/extensions.Echo/Unary" },
            CancellationToken.None);
        await lease.Stream.Outbound.ReadAsync();

        await call.WriteAsync(
            new ExtensionRpcMessage
            {
                Data = new ExtensionRpcData { Payload = ByteString.CopyFrom([1, 2]) },
            },
            CancellationToken.None);
        await lease.Stream.Outbound.ReadAsync();

        ValueTask blockedWrite = call.WriteAsync(
            new ExtensionRpcMessage
            {
                Data = new ExtensionRpcData { Payload = ByteString.CopyFrom([3, 4]) },
            },
            CancellationToken.None);
        Assert.False(blockedWrite.IsCompleted);

        await lease.Stream.HandleInboundAsync(
            new ExtensionRpcMessage
            {
                SessionId = hello.SessionId,
                ShardId = hello.ShardId,
                CallId = call.CallId,
                WindowUpdate = new ExtensionRpcWindowUpdate { ByteCount = 2 },
            },
            CancellationToken.None);

        await blockedWrite;
        ExtensionRpcMessage data = await lease.Stream.Outbound.ReadAsync();
        Assert.Equal(ByteString.CopyFrom([3, 4]), data.Data.Payload);
    }

    [Fact]
    public async Task RelayAsync_ClosesCoordinatorWhenRuntimeStreamEnds()
    {
        var streamCoordinator = new ExtensionRpcStreamCoordinator();
        var endpoints = new WorkerProxyEndpointConfiguration(
            Options.Create(new WorkerProxyOptions()));
        var relay = new ExtensionRpcRelay(endpoints, streamCoordinator);
        Channel<ExtensionRpcMessage> inbound = Channel.CreateUnbounded<ExtensionRpcMessage>();
        var outbound = new TestServerStreamWriter<ExtensionRpcMessage>();

        Task relayTask = relay.RelayAsync(
            new TestAsyncStreamReader<ExtensionRpcMessage>(inbound.Reader),
            outbound,
            CancellationToken.None);

        ExtensionRpcMessage hello = await outbound.Messages.ReadAsync();
        Assert.Equal(ExtensionRpcMessage.ContentOneofCase.Hello, hello.ContentCase);
        Assert.True(streamCoordinator.HasConnectedStream);

        inbound.Writer.TryComplete();
        await relayTask;

        Assert.False(streamCoordinator.HasConnectedStream);
    }

    [Fact]
    public async Task RelayAsync_RejectsSecondConcurrentPhysicalStreamWithAlreadyExists()
    {
        var streamCoordinator = new ExtensionRpcStreamCoordinator();
        var endpoints = new WorkerProxyEndpointConfiguration(
            Options.Create(new WorkerProxyOptions()));
        var relay = new ExtensionRpcRelay(endpoints, streamCoordinator);
        Channel<ExtensionRpcMessage> firstInbound = Channel.CreateUnbounded<ExtensionRpcMessage>();
        var firstOutbound = new TestServerStreamWriter<ExtensionRpcMessage>();
        Task firstRelayTask = relay.RelayAsync(
            new TestAsyncStreamReader<ExtensionRpcMessage>(firstInbound.Reader),
            firstOutbound,
            CancellationToken.None);
        await firstOutbound.Messages.ReadAsync();

        GrpcRpcException exception = await Assert.ThrowsAsync<GrpcRpcException>(
            () => relay.RelayAsync(
                new TestAsyncStreamReader<ExtensionRpcMessage>(
                    Channel.CreateUnbounded<ExtensionRpcMessage>().Reader),
                new TestServerStreamWriter<ExtensionRpcMessage>(),
                CancellationToken.None));

        Assert.Equal(StatusCode.AlreadyExists, exception.StatusCode);

        firstInbound.Writer.TryComplete();
        await firstRelayTask;
    }

    private static ExtensionRpcMessage CreateReady(ExtensionRpcMessage hello)
    {
        return new ExtensionRpcMessage
        {
            SessionId = hello.SessionId,
            ShardId = hello.ShardId,
            Ready = new ExtensionRpcReady
            {
                SelectedVersion = ExtensionRpcStreamCoordinator.ProtocolVersion,
                Enabled = true,
                InitialReceiveWindowBytes = ExtensionRpcStreamCoordinator.DefaultInitialWindowSize,
                MaxDataChunkBytes = ExtensionRpcStreamCoordinator.DefaultMaxChunkSize,
                MaxMessageBytes = ExtensionRpcStreamCoordinator.DefaultMaxMessageSize,
            },
        };
    }

    private static async Task<T> FirstAsync<T>(IAsyncEnumerable<T> source)
    {
        await foreach (T item in source)
        {
            return item;
        }

        throw new InvalidOperationException("The sequence contained no items.");
    }

    private sealed class TestAsyncStreamReader<T>(ChannelReader<T> reader) : IAsyncStreamReader<T>
    {
        public T Current { get; private set; } = default!;

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            while (await reader.WaitToReadAsync(cancellationToken))
            {
                if (reader.TryRead(out T? item))
                {
                    Current = item;
                    return true;
                }
            }

            return false;
        }
    }

    private sealed class TestServerStreamWriter<T> : IServerStreamWriter<T>
    {
        private readonly Channel<T> _messages = Channel.CreateUnbounded<T>();

        public ChannelReader<T> Messages => _messages.Reader;

        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(T message)
        {
            return _messages.Writer.WriteAsync(message).AsTask();
        }

        public Task WriteAsync(T message, CancellationToken cancellationToken)
        {
            return _messages.Writer.WriteAsync(message, cancellationToken).AsTask();
        }
    }
}
