// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.ExtensionRpc;
using Google.Protobuf;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public class ExtensionGrpcIngressTests
{
    private const int WorkerGrpcPort = 50052;
    private static readonly ExtensionGrpcMetrics Metrics = new(new TestMeterFactory());

    [Fact]
    public async Task HandleAsync_RelaysOpaqueGrpcFramesAndMetadata()
    {
        var streamCoordinator = new ExtensionRpcStreamCoordinator();
        await using ExtensionRpcStreamLease lease = streamCoordinator.Open(CancellationToken.None);
        ExtensionRpcMessage hello = await lease.Stream.Outbound.ReadAsync();
        await lease.Stream.HandleInboundAsync(CreateReady(hello), CancellationToken.None);

        byte[] requestPayload = Encoding.UTF8.GetBytes("request");
        var context = CreateContext(CreateGrpcFrame(requestPayload));
        context.Request.Headers.Append("trace-bin", "AQI");
        context.Request.Headers.Append("trace-bin", "AwQ=");
        context.Request.Headers.Append("trace-bin", "BQY,Bwg=");
        context.Request.Headers.Append("request-text", "request-value");
        var trailersFeature = new TestResponseTrailersFeature();
        context.Features.Set<IHttpResponseTrailersFeature>(trailersFeature);

        ExtensionGrpcIngress ingress = CreateIngress(streamCoordinator);
        using var activity = new Activity("extension-rpc-test").Start();
        Task ingressTask = ingress.HandleAsync(context);

        ExtensionRpcMessage start = await lease.Stream.Outbound.ReadAsync();
        ExtensionRpcMessage data = await lease.Stream.Outbound.ReadAsync();
        ExtensionRpcMessage halfClose = await lease.Stream.Outbound.ReadAsync();

        Assert.Equal("/extensions.Echo/Unary", start.Start.Method);
        Assert.Collection(
            start.Start.Metadata.Where(entry => string.Equals(
                entry.Key, "trace-bin", StringComparison.OrdinalIgnoreCase)),
            entry => Assert.Equal(ByteString.CopyFrom([1, 2]), entry.Value),
            entry => Assert.Equal(ByteString.CopyFrom([3, 4]), entry.Value),
            entry => Assert.Equal(ByteString.CopyFrom([5, 6]), entry.Value),
            entry => Assert.Equal(ByteString.CopyFrom([7, 8]), entry.Value));
        Assert.Equal(
            "request-value",
            Assert.Single(start.Start.Metadata, entry => string.Equals(
                entry.Key, "request-text", StringComparison.OrdinalIgnoreCase)).Value.ToStringUtf8());
        Assert.Equal(ByteString.CopyFrom(requestPayload), data.Data.Payload);
        Assert.Equal((ulong)requestPayload.Length, data.Data.MessageLength);
        Assert.True(data.Data.EndOfMessage);
        Assert.Equal(ExtensionRpcMessage.ContentOneofCase.HalfClose, halfClose.ContentCase);

        byte[] responsePayload = Encoding.UTF8.GetBytes("response");
        await lease.Stream.HandleInboundAsync(new ExtensionRpcMessage
        {
            SessionId = hello.SessionId,
            ShardId = hello.ShardId,
            CallId = start.CallId,
            Headers = new ExtensionRpcHeaders
            {
                Metadata =
                {
                    new ExtensionRpcMetadataEntry
                    {
                        Key = "response-bin",
                        Value = ByteString.CopyFrom([9, 10]),
                    },
                },
            },
        }, CancellationToken.None);
        await lease.Stream.HandleInboundAsync(new ExtensionRpcMessage
        {
            SessionId = hello.SessionId,
            ShardId = hello.ShardId,
            CallId = start.CallId,
            Data = new ExtensionRpcData
            {
                MessageId = 1,
                MessageLength = (ulong)responsePayload.Length,
                Payload = ByteString.CopyFrom(responsePayload),
                EndOfMessage = true,
            },
        }, CancellationToken.None);
        await lease.Stream.HandleInboundAsync(new ExtensionRpcMessage
        {
            SessionId = hello.SessionId,
            ShardId = hello.ShardId,
            CallId = start.CallId,
            Complete = new ExtensionRpcComplete
            {
                Status = ExtensionRpcStatus.Ok,
                Trailers =
                {
                    new ExtensionRpcMetadataEntry
                    {
                        Key = "trailer-bin",
                        Value = ByteString.CopyFrom([11, 12]),
                    },
                },
            },
        }, CancellationToken.None);

        await ingressTask;

        Assert.Equal(CreateGrpcFrame(responsePayload), ((MemoryStream)context.Response.Body).ToArray());
        Assert.Equal("CQo=", context.Response.Headers["response-bin"]);
        Assert.Equal("Cww=", trailersFeature.Trailers["trailer-bin"]);
        Assert.Equal("0", trailersFeature.Trailers["grpc-status"]);
        Assert.Equal(start.CallId, activity.GetTagItem("azure.functions.worker_proxy.extension_rpc.call_id"));
        Assert.Equal(hello.ShardId, activity.GetTagItem("azure.functions.worker_proxy.extension_rpc.stream_id"));
        Assert.Equal(1, activity.GetTagItem("azure.functions.worker_proxy.extension_rpc.active_calls_at_open"));
        Assert.Equal(1, activity.GetTagItem("azure.functions.worker_proxy.extension_rpc.active_calls_at_completion"));
        Assert.Null(activity.GetTagItem("azure.functions.worker_proxy.extension_rpc.call.open.duration_ms"));
        Assert.Null(activity.GetTagItem("azure.functions.worker_proxy.extension_rpc.call.duration_ms"));
    }

    [Fact]
    public async Task HandleAsync_InvalidRequestFrameStopsRelaysBeforeCompletingResponse()
    {
        var streamCoordinator = new ExtensionRpcStreamCoordinator();
        await using ExtensionRpcStreamLease lease = streamCoordinator.Open(CancellationToken.None);
        ExtensionRpcMessage hello = await lease.Stream.Outbound.ReadAsync();
        await lease.Stream.HandleInboundAsync(CreateReady(hello), CancellationToken.None);

        byte[] truncatedFrame = new byte[7];
        BinaryPrimitives.WriteUInt32BigEndian(truncatedFrame.AsSpan(1, 4), 10);
        var context = CreateContext(truncatedFrame);
        ExtensionGrpcIngress ingress = CreateIngress(streamCoordinator);

        Task ingressTask = ingress.HandleAsync(context);

        ExtensionRpcMessage start = await lease.Stream.Outbound.ReadAsync();
        ExtensionRpcMessage cancel = await lease.Stream.Outbound.ReadAsync();
        await ingressTask;

        Assert.Equal(ExtensionRpcMessage.ContentOneofCase.Start, start.ContentCase);
        Assert.Equal(ExtensionRpcMessage.ContentOneofCase.Cancel, cancel.ContentCase);
        Assert.Equal("13", context.Response.Headers["grpc-status"]);
    }

    [Fact]
    public async Task HandleAsync_ResponseExceedingNegotiatedLimitReturnsInternal()
    {
        var streamCoordinator = new ExtensionRpcStreamCoordinator();
        await using ExtensionRpcStreamLease lease = streamCoordinator.Open(CancellationToken.None);
        ExtensionRpcMessage hello = await lease.Stream.Outbound.ReadAsync();
        ExtensionRpcMessage ready = CreateReady(hello);
        ready.Ready.MaxDataChunkBytes = 4;
        ready.Ready.MaxMessageBytes = 4;
        await lease.Stream.HandleInboundAsync(ready, CancellationToken.None);

        var context = CreateContext(CreateGrpcFrame([1]));
        ExtensionGrpcIngress ingress = CreateIngress(streamCoordinator);

        Task ingressTask = ingress.HandleAsync(context);
        ExtensionRpcMessage start = await lease.Stream.Outbound.ReadAsync();
        await lease.Stream.Outbound.ReadAsync();
        await lease.Stream.Outbound.ReadAsync();
        await lease.Stream.HandleInboundAsync(
            new ExtensionRpcMessage
            {
                SessionId = hello.SessionId,
                ShardId = hello.ShardId,
                CallId = start.CallId,
                Data = new ExtensionRpcData
                {
                    MessageId = 1,
                    MessageLength = 5,
                    Payload = ByteString.CopyFrom(new byte[5]),
                    EndOfMessage = true,
                },
            },
            CancellationToken.None);

        await ingressTask;

        Assert.Equal("13", context.Response.Headers["grpc-status"]);
    }

    [Fact]
    public async Task HandleAsync_ResponseChunkExceedingDeclaredLengthWritesNoFrame()
    {
        var streamCoordinator = new ExtensionRpcStreamCoordinator();
        await using ExtensionRpcStreamLease lease = streamCoordinator.Open(CancellationToken.None);
        ExtensionRpcMessage hello = await lease.Stream.Outbound.ReadAsync();
        await lease.Stream.HandleInboundAsync(CreateReady(hello), CancellationToken.None);
        var context = CreateContext(CreateGrpcFrame([1]));
        ExtensionGrpcIngress ingress = CreateIngress(streamCoordinator);

        Task ingressTask = ingress.HandleAsync(context);
        ExtensionRpcMessage start = await lease.Stream.Outbound.ReadAsync();
        await lease.Stream.Outbound.ReadAsync();
        await lease.Stream.Outbound.ReadAsync();
        await lease.Stream.HandleInboundAsync(
            new ExtensionRpcMessage
            {
                SessionId = hello.SessionId,
                ShardId = hello.ShardId,
                CallId = start.CallId,
                Data = new ExtensionRpcData
                {
                    MessageId = 1,
                    MessageLength = 1,
                    Payload = ByteString.CopyFrom([1, 2]),
                    EndOfMessage = true,
                },
            },
            CancellationToken.None);

        await ingressTask;

        Assert.Empty(((MemoryStream)context.Response.Body).ToArray());
        Assert.Equal("13", context.Response.Headers["grpc-status"]);
    }

    [Fact]
    public async Task HandleAsync_DeadlineExpiresWhileWaitingForNegotiation()
    {
        var streamCoordinator = new ExtensionRpcStreamCoordinator();
        await using ExtensionRpcStreamLease lease = streamCoordinator.Open(CancellationToken.None);
        await lease.Stream.Outbound.ReadAsync();
        var context = CreateContext([]);
        context.Request.Headers["grpc-timeout"] = "10m";
        ExtensionGrpcIngress ingress = CreateIngress(streamCoordinator);

        await ingress.HandleAsync(context);

        Assert.Equal("4", context.Response.Headers["grpc-status"]);
    }

    [Fact]
    public async Task HandleAsync_StreamDisconnectReturnsUnavailable()
    {
        var streamCoordinator = new ExtensionRpcStreamCoordinator();
        await using ExtensionRpcStreamLease lease = streamCoordinator.Open(CancellationToken.None);
        ExtensionRpcMessage hello = await lease.Stream.Outbound.ReadAsync();
        await lease.Stream.HandleInboundAsync(CreateReady(hello), CancellationToken.None);

        var context = CreateContext(CreateGrpcFrame(Encoding.UTF8.GetBytes("request")));
        ExtensionGrpcIngress ingress = CreateIngress(streamCoordinator);

        Task ingressTask = ingress.HandleAsync(context);
        await lease.Stream.Outbound.ReadAsync();
        await lease.Stream.Outbound.ReadAsync();
        await lease.Stream.Outbound.ReadAsync();

        await lease.DisposeAsync();
        await ingressTask;

        Assert.Equal("14", context.Response.Headers["grpc-status"]);
    }

    [Fact]
    public async Task HandleAsync_HostCancellationReturnsCancelled()
    {
        var streamCoordinator = new ExtensionRpcStreamCoordinator();
        await using ExtensionRpcStreamLease lease = streamCoordinator.Open(CancellationToken.None);
        ExtensionRpcMessage hello = await lease.Stream.Outbound.ReadAsync();
        await lease.Stream.HandleInboundAsync(CreateReady(hello), CancellationToken.None);

        var context = CreateContext([]);
        ExtensionGrpcIngress ingress = CreateIngress(streamCoordinator);

        Task ingressTask = ingress.HandleAsync(context);
        ExtensionRpcMessage start = await lease.Stream.Outbound.ReadAsync();
        await lease.Stream.Outbound.ReadAsync();
        await lease.Stream.HandleInboundAsync(
            new ExtensionRpcMessage
            {
                SessionId = hello.SessionId,
                ShardId = hello.ShardId,
                CallId = start.CallId,
                Cancel = new ExtensionRpcCancel { Detail = "Cancelled by the host." },
            },
            CancellationToken.None);

        await ingressTask;

        Assert.Equal("1", context.Response.Headers["grpc-status"]);
        Assert.Equal("Cancelled%20by%20the%20host.", context.Response.Headers["grpc-message"]);
    }

    [Fact]
    public void CalculateRequestBufferSize_RejectsUnsupportedSize()
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => ExtensionGrpcIngress.CalculateRequestBufferSize(
                (ulong)int.MaxValue + 1,
                (ulong)int.MaxValue + 1));

        Assert.Contains("exceeds the supported limit", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1n", 1)]
    [InlineData("99n", 1)]
    [InlineData("100n", 1)]
    [InlineData("101n", 2)]
    [InlineData("1u", 10)]
    public void TryParseTimeout_RoundsPositiveSubTickDurationsUp(string value, long expectedTicks)
    {
        Assert.True(ExtensionGrpcIngress.TryParseTimeout(
            value,
            out Google.Protobuf.WellKnownTypes.Duration? timeout));
        Google.Protobuf.WellKnownTypes.Duration parsedTimeout = Assert.IsType<Google.Protobuf.WellKnownTypes.Duration>(timeout);
        Assert.Equal(expectedTicks, parsedTimeout.ToTimeSpan().Ticks);
    }

    [Theory]
    [InlineData("99999999H", false)]
    [InlineData("99999999M", true)]
    [InlineData("99999999S", true)]
    [InlineData("99999999m", true)]
    [InlineData("99999999u", true)]
    [InlineData("99999999n", true)]
    public void TryParseTimeout_MaximumDigitCountDoesNotOverflow(string value, bool representable)
    {
        bool parsed = ExtensionGrpcIngress.TryParseTimeout(
            value,
            out Google.Protobuf.WellKnownTypes.Duration? timeout);

        Assert.Equal(representable, parsed);
        Assert.Equal(representable, timeout is not null);
    }

    [Theory]
    [InlineData("/AzureFunctionsRpcMessages.FunctionRpc/EventStream", false)]
    [InlineData("/AzureFunctionsExtensionRpcMessages.ExtensionRpc/EventStream", false)]
    [InlineData("/extensions.Echo/Unary", true)]
    public void CanHandle_ReservesRpcEventStreamRoutes(string path, bool expected)
    {
        var context = CreateContext([]);
        context.Request.Path = path;

        Assert.Equal(expected, CreateIngress(new ExtensionRpcStreamCoordinator()).CanHandle(context));
    }

    [Theory]
    [InlineData("application/grpc", true)]
    [InlineData("application/grpc+proto", true)]
    [InlineData("application/grpc; charset=utf-8", true)]
    [InlineData("application/grpc-web", false)]
    [InlineData("application/grpcanything", false)]
    public void CanHandle_AcceptsOnlyNativeGrpcContentTypes(string contentType, bool expected)
    {
        var context = CreateContext([]);
        context.Request.ContentType = contentType;

        Assert.Equal(expected, CreateIngress(new ExtensionRpcStreamCoordinator()).CanHandle(context));
    }

    private static ExtensionGrpcIngress CreateIngress(ExtensionRpcStreamCoordinator streamCoordinator)
    {
        var endpoints = new WorkerProxyEndpointConfiguration(Options.Create(new WorkerProxyOptions
        {
            ManagementPort = 50050,
            RuntimeGrpcPort = 50051,
            WorkerGrpcPort = WorkerGrpcPort,
        }));
        endpoints.Configure(new KestrelServerOptions());

        return new ExtensionGrpcIngress(
            endpoints,
            streamCoordinator,
            Metrics,
            NullLogger<ExtensionGrpcIngress>.Instance);
    }

    private static DefaultHttpContext CreateContext(byte[] requestBody)
    {
        var context = new DefaultHttpContext();
        context.Connection.LocalPort = WorkerGrpcPort;
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/extensions.Echo/Unary";
        context.Request.ContentType = "application/grpc";
        context.Request.Body = new MemoryStream(requestBody);
        context.Response.Body = new MemoryStream();

        return context;
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

    private static byte[] CreateGrpcFrame(byte[] payload)
    {
        byte[] frame = new byte[payload.Length + 5];
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(1, 4), (uint)payload.Length);
        payload.CopyTo(frame, 5);

        return frame;
    }

    private sealed class TestResponseTrailersFeature : IHttpResponseTrailersFeature
    {
        public IHeaderDictionary Trailers { get; set; } = new HeaderDictionary();
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);

        public void Dispose()
        {
        }
    }
}
