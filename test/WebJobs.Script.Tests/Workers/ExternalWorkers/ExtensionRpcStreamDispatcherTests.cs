// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.ExternalWorkers;

public class ExtensionRpcStreamDispatcherTests
{
    [Fact]
    public async Task HandleAsync_DispatchesOpaqueGrpcCall()
    {
        byte[] requestPayload = Encoding.UTF8.GetBytes("request");
        byte[] responsePayload = Encoding.UTF8.GetBytes("response");
        var outbound = Channel.CreateUnbounded<ExtensionRpcMessage>();
        await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var endpoint = new ExtensionRpcEndpoint(
            async context =>
            {
                byte[] requestFrame = new byte[requestPayload.Length + 5];
                await context.Request.Body.ReadExactlyAsync(requestFrame);
                Assert.Equal(CreateGrpcFrame(requestPayload), requestFrame);

                context.Response.ContentType = "application/grpc";
                context.Response.Headers["x-extension-header"] = "header-value";
                context.Response.Headers["trace-bin"] = "AQI,AwQ=";
                await context.Response.Body.WriteAsync(CreateGrpcFrame(responsePayload));
                context.Response.AppendTrailer("grpc-status", "0");
                context.Response.AppendTrailer("x-extension-trailer", "trailer-value");
            },
            services);
        var dispatcher = new ExtensionRpcStreamDispatcher(
            "worker-1",
            new TestEndpointRouter(endpoint),
            outbound.Writer,
            NullLogger.Instance);

        const string sessionId = "session-1";
        const string callId = "call-1";
        await dispatcher.HandleAsync(
            new ExtensionRpcMessage
            {
                SessionId = sessionId,
                ShardId = "shard-1",
                Hello = new ExtensionRpcHello
                {
                    SupportedVersions = { ExtensionRpcStreamDispatcher.ProtocolVersion },
                    InitialReceiveWindowBytes = ExtensionRpcStreamDispatcher.DefaultInitialWindowSize,
                    MaxDataChunkBytes = ExtensionRpcStreamDispatcher.DefaultMaxChunkSize,
                    MaxMessageBytes = ExtensionRpcStreamDispatcher.DefaultMaxMessageSize,
                },
            },
            CancellationToken.None);

        ExtensionRpcMessage ready = await outbound.Reader.ReadAsync();
        Assert.True(ready.Ready.Enabled);

        await dispatcher.HandleAsync(
            new ExtensionRpcMessage
            {
                SessionId = sessionId,
                ShardId = "shard-1",
                CallId = callId,
                Start = new ExtensionRpcStart
                {
                    Method = "/extensions.Echo/Unary",
                },
            },
            CancellationToken.None);
        await dispatcher.HandleAsync(
            new ExtensionRpcMessage
            {
                SessionId = sessionId,
                ShardId = "shard-1",
                CallId = callId,
                Data = new ExtensionRpcData
                {
                    MessageId = 1,
                    MessageLength = (ulong)requestPayload.Length,
                    Payload = ByteString.CopyFrom(requestPayload),
                    EndOfMessage = true,
                },
            },
            CancellationToken.None);
        await dispatcher.HandleAsync(
            new ExtensionRpcMessage
            {
                SessionId = sessionId,
                ShardId = "shard-1",
                CallId = callId,
                HalfClose = new ExtensionRpcHalfClose(),
            },
            CancellationToken.None);

        List<ExtensionRpcMessage> responses = [];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (responses.Count is 0 || responses[^1].ContentCase is not ExtensionRpcMessage.ContentOneofCase.Complete)
        {
            ExtensionRpcMessage response = await outbound.Reader.ReadAsync(timeout.Token);
            responses.Add(response);
        }

        Assert.Collection(
            responses,
            windowUpdate =>
            {
                Assert.Equal(ExtensionRpcMessage.ContentOneofCase.WindowUpdate, windowUpdate.ContentCase);
                Assert.Equal((ulong)requestPayload.Length, windowUpdate.WindowUpdate.ByteCount);
            },
            headers =>
            {
                Assert.Equal(ExtensionRpcMessage.ContentOneofCase.Headers, headers.ContentCase);
                Assert.Contains(
                    headers.Headers.Metadata,
                    entry => string.Equals(entry.Key, "x-extension-header", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(entry.Value.ToStringUtf8(), "header-value", StringComparison.Ordinal));
                Assert.DoesNotContain(
                    headers.Headers.Metadata,
                    entry => string.Equals(entry.Key, "content-type", StringComparison.OrdinalIgnoreCase));
                Assert.Collection(
                    headers.Headers.Metadata.Where(entry => string.Equals(
                        entry.Key, "trace-bin", StringComparison.OrdinalIgnoreCase)),
                    entry => Assert.Equal(ByteString.CopyFrom([1, 2]), entry.Value),
                    entry => Assert.Equal(ByteString.CopyFrom([3, 4]), entry.Value));
            },
            data =>
            {
                Assert.Equal(ExtensionRpcMessage.ContentOneofCase.Data, data.ContentCase);
                Assert.Equal(ByteString.CopyFrom(responsePayload), data.Data.Payload);
                Assert.Equal((ulong)responsePayload.Length, data.Data.MessageLength);
            },
            complete =>
            {
                Assert.Equal(ExtensionRpcStatus.Ok, complete.Complete.Status);
                Assert.Contains(
                    complete.Complete.Trailers,
                    entry => string.Equals(entry.Key, "x-extension-trailer", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(entry.Value.ToStringUtf8(), "trailer-value", StringComparison.Ordinal));
            });
    }

    [Fact]
    public async Task HandleAsync_ResponseWaitsForWindowUpdate()
    {
        byte[] responsePayload = Encoding.UTF8.GetBytes("response");
        var outbound = Channel.CreateUnbounded<ExtensionRpcMessage>();
        await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var endpoint = new ExtensionRpcEndpoint(
            async context =>
            {
                await context.Response.Body.WriteAsync(CreateGrpcFrame(responsePayload));
                context.Response.AppendTrailer("grpc-status", "0");
            },
            services);
        var dispatcher = new ExtensionRpcStreamDispatcher(
            "worker-1",
            new TestEndpointRouter(endpoint),
            outbound.Writer,
            NullLogger.Instance);

        const string sessionId = "session-1";
        const string callId = "call-1";
        await dispatcher.HandleAsync(
            CreateHello(sessionId, initialWindow: 4, maxChunkSize: 8),
            CancellationToken.None);
        await outbound.Reader.ReadAsync();
        await dispatcher.HandleAsync(
            CreateStart(sessionId, callId, timeout: null),
            CancellationToken.None);
        await dispatcher.HandleAsync(
            new ExtensionRpcMessage
            {
                SessionId = sessionId,
                ShardId = "shard-1",
                CallId = callId,
                HalfClose = new ExtensionRpcHalfClose(),
            },
            CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        ExtensionRpcMessage headers = await outbound.Reader.ReadAsync(timeout.Token);
        ExtensionRpcMessage firstData = await outbound.Reader.ReadAsync(timeout.Token);

        Assert.Equal(ExtensionRpcMessage.ContentOneofCase.Headers, headers.ContentCase);
        Assert.Equal(4, firstData.Data.Payload.Length);
        Assert.False(outbound.Reader.TryRead(out _));

        await dispatcher.HandleAsync(
            new ExtensionRpcMessage
            {
                SessionId = sessionId,
                ShardId = "shard-1",
                CallId = callId,
                WindowUpdate = new ExtensionRpcWindowUpdate
                {
                    ByteCount = 4,
                },
            },
            CancellationToken.None);

        ExtensionRpcMessage secondData = await outbound.Reader.ReadAsync(timeout.Token);
        ExtensionRpcMessage complete = await outbound.Reader.ReadAsync(timeout.Token);
        Assert.Equal(4, secondData.Data.Payload.Length);
        Assert.True(secondData.Data.EndOfMessage);
        Assert.Equal(ExtensionRpcMessage.ContentOneofCase.Complete, complete.ContentCase);
    }

    [Fact]
    public async Task HandleAsync_PropagatesDeadline()
    {
        var outbound = Channel.CreateUnbounded<ExtensionRpcMessage>();
        await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var endpoint = new ExtensionRpcEndpoint(
            context => Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted),
            services);
        var dispatcher = new ExtensionRpcStreamDispatcher(
            "worker-1",
            new TestEndpointRouter(endpoint),
            outbound.Writer,
            NullLogger.Instance);

        const string sessionId = "session-1";
        const string callId = "call-1";
        await dispatcher.HandleAsync(
            CreateHello(
                sessionId,
                ExtensionRpcStreamDispatcher.DefaultInitialWindowSize,
                ExtensionRpcStreamDispatcher.DefaultMaxChunkSize),
            CancellationToken.None);
        await outbound.Reader.ReadAsync();
        await dispatcher.HandleAsync(
            CreateStart(sessionId, callId, Duration.FromTimeSpan(TimeSpan.FromMilliseconds(10))),
            CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        ExtensionRpcMessage complete = await outbound.Reader.ReadAsync(timeout.Token);
        Assert.Equal(ExtensionRpcStatus.DeadlineExceeded, complete.Complete.Status);
    }

    [Fact]
    public async Task HandleAsync_CancelledTerminalWriteRetriesWithFailureStatus()
    {
        var outbound = Channel.CreateBounded<ExtensionRpcMessage>(1);
        await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var endpoint = new ExtensionRpcEndpoint(
            context =>
            {
                context.Response.AppendTrailer("grpc-status", "0");
                return Task.CompletedTask;
            },
            services);
        var dispatcher = new ExtensionRpcStreamDispatcher(
            "worker-1",
            new TestEndpointRouter(endpoint),
            outbound.Writer,
            NullLogger.Instance);

        const string sessionId = "session-1";
        await dispatcher.HandleAsync(
            CreateHello(
                sessionId,
                ExtensionRpcStreamDispatcher.DefaultInitialWindowSize,
                ExtensionRpcStreamDispatcher.DefaultMaxChunkSize),
            CancellationToken.None);
        await outbound.Reader.ReadAsync();
        await dispatcher.HandleAsync(
            CreateStart(sessionId, "call-1", Duration.FromTimeSpan(TimeSpan.FromMilliseconds(50))),
            CancellationToken.None);

        await Task.Delay(100);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        ExtensionRpcMessage headers = await outbound.Reader.ReadAsync(timeout.Token);
        ExtensionRpcMessage complete = await outbound.Reader.ReadAsync(timeout.Token);

        Assert.Equal(ExtensionRpcMessage.ContentOneofCase.Headers, headers.ContentCase);
        Assert.Equal(ExtensionRpcStatus.DeadlineExceeded, complete.Complete.Status);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(3652500)]
    public async Task HandleAsync_VeryLongDeadlineDoesNotUseUnsupportedCancellationTimer(int days)
    {
        var outbound = Channel.CreateUnbounded<ExtensionRpcMessage>();
        await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var endpoint = new ExtensionRpcEndpoint(
            context =>
            {
                context.Response.AppendTrailer("grpc-status", "0");
                return Task.CompletedTask;
            },
            services);
        var dispatcher = new ExtensionRpcStreamDispatcher(
            "worker-1",
            new TestEndpointRouter(endpoint),
            outbound.Writer,
            NullLogger.Instance);

        const string sessionId = "session-1";
        const string callId = "call-1";
        await dispatcher.HandleAsync(
            CreateHello(
                sessionId,
                ExtensionRpcStreamDispatcher.DefaultInitialWindowSize,
                ExtensionRpcStreamDispatcher.DefaultMaxChunkSize),
            CancellationToken.None);
        await outbound.Reader.ReadAsync();
        await dispatcher.HandleAsync(
            CreateStart(sessionId, callId, Duration.FromTimeSpan(TimeSpan.FromDays(days))),
            CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        ExtensionRpcMessage headers = await outbound.Reader.ReadAsync(timeout.Token);
        ExtensionRpcMessage complete = await outbound.Reader.ReadAsync(timeout.Token);
        Assert.Equal(ExtensionRpcMessage.ContentOneofCase.Headers, headers.ContentCase);
        Assert.Equal(ExtensionRpcStatus.Ok, complete.Complete.Status);
    }

    [Fact]
    public async Task HandleAsync_BlockedFailureStatusReleasesEndpointLease()
    {
        var outbound = Channel.CreateBounded<ExtensionRpcMessage>(1);
        var leaseReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var endpoint = new ExtensionRpcEndpoint(
            _ => throw new InvalidOperationException("Test endpoint failure."),
            services,
            CancellationToken.None,
            () =>
            {
                leaseReleased.TrySetResult();
                return ValueTask.CompletedTask;
            });
        var dispatcher = new ExtensionRpcStreamDispatcher(
            "worker-1",
            new TestEndpointRouter(endpoint),
            outbound.Writer,
            NullLogger.Instance);

        const string sessionId = "session-1";
        await dispatcher.HandleAsync(
            CreateHello(
                sessionId,
                ExtensionRpcStreamDispatcher.DefaultInitialWindowSize,
                ExtensionRpcStreamDispatcher.DefaultMaxChunkSize),
            CancellationToken.None);
        await outbound.Reader.ReadAsync();
        Assert.True(outbound.Writer.TryWrite(new ExtensionRpcMessage()));

        await dispatcher.HandleAsync(
            CreateStart(sessionId, "call-1", timeout: null),
            CancellationToken.None);

        await leaseReleased.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, dispatcher.ActiveCallCount);
    }

    [Fact]
    public async Task HandleAsync_MalformedRequestReleasesCall()
    {
        var outbound = Channel.CreateUnbounded<ExtensionRpcMessage>();
        await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var endpoint = new ExtensionRpcEndpoint(
            async context =>
            {
                var buffer = new byte[5];
                await context.Request.Body.ReadExactlyAsync(buffer);
            },
            services);
        var dispatcher = new ExtensionRpcStreamDispatcher(
            "worker-1",
            new TestEndpointRouter(endpoint),
            outbound.Writer,
            NullLogger.Instance);
        const string sessionId = "session-1";
        const string callId = "call-1";
        await dispatcher.HandleAsync(
            CreateHello(
                sessionId,
                ExtensionRpcStreamDispatcher.DefaultInitialWindowSize,
                ExtensionRpcStreamDispatcher.DefaultMaxChunkSize),
            CancellationToken.None);
        await outbound.Reader.ReadAsync();
        await dispatcher.HandleAsync(
            CreateStart(sessionId, callId, timeout: null),
            CancellationToken.None);

        await dispatcher.HandleAsync(
            new ExtensionRpcMessage
            {
                SessionId = sessionId,
                ShardId = "shard-1",
                CallId = callId,
                Data = new ExtensionRpcData
                {
                    MessageId = 1,
                    MessageLength = 1,
                    Payload = ByteString.CopyFrom([0x01, 0x02]),
                    EndOfMessage = true,
                },
            },
            CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        ExtensionRpcMessage complete = await outbound.Reader.ReadAsync(timeout.Token);
        Assert.Equal(ExtensionRpcStatus.Internal, complete.Complete.Status);
        Assert.Equal(0, dispatcher.ActiveCallCount);
    }

    private static byte[] CreateGrpcFrame(byte[] payload)
    {
        byte[] frame = new byte[payload.Length + 5];
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(1, 4), (uint)payload.Length);
        payload.CopyTo(frame, 5);

        return frame;
    }

    private static ExtensionRpcMessage CreateHello(string sessionId, ulong initialWindow, uint maxChunkSize)
    {
        return new ExtensionRpcMessage
        {
            SessionId = sessionId,
            ShardId = "shard-1",
            Hello = new ExtensionRpcHello
            {
                SupportedVersions = { ExtensionRpcStreamDispatcher.ProtocolVersion },
                InitialReceiveWindowBytes = initialWindow,
                MaxDataChunkBytes = maxChunkSize,
                MaxMessageBytes = ExtensionRpcStreamDispatcher.DefaultMaxMessageSize,
            },
        };
    }

    private static ExtensionRpcMessage CreateStart(string sessionId, string callId, Duration? timeout)
    {
        var message = new ExtensionRpcMessage
        {
            SessionId = sessionId,
            ShardId = "shard-1",
            CallId = callId,
            Start = new ExtensionRpcStart
            {
                Method = "/extensions.Echo/Unary",
            },
        };

        if (timeout is not null)
        {
            message.Start.Timeout = timeout;
        }

        return message;
    }

    private sealed class TestEndpointRouter(ExtensionRpcEndpoint endpoint) : IExtensionRpcEndpointRouter
    {
        public ValueTask<ExtensionRpcEndpoint?> RouteAsync(
            string workerId,
            string method,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<ExtensionRpcEndpoint?>(endpoint);
        }
    }
}
