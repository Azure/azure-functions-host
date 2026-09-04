using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.Azure.Functions.WorkerProxy.Tests;

public class ExtensionRpcStreamCoordinatorTests
{
    private static readonly ExtensionGrpcMetrics Metrics = new(new TestMeterFactory());

    [Fact]
    public async Task OpenExtensionCall_RoutesMessagesThroughSession()
    {
        var streamCoordinator = new ExtensionRpcStreamCoordinator();
        await using ExtensionRpcStreamLease lease =
            streamCoordinator.Open(CancellationToken.None);

        ExtensionRpcMessage hello = await lease.Stream.Outbound.ReadAsync();
        string sessionId = hello.SessionId;
        Assert.Contains(ExtensionRpcStreamCoordinator.ProtocolVersion, hello.Hello.SupportedVersions);

        await lease.Stream.HandleInboundAsync(CreateReady(hello), CancellationToken.None);

        await using ExtensionCall call =
            await streamCoordinator.OpenExtensionCallAsync(
            new ExtensionRpcStart { Method = "/extensions.Echo/Unary" },
            CancellationToken.None);

        ExtensionRpcMessage start = await lease.Stream.Outbound.ReadAsync();
        Assert.Equal(sessionId, start.SessionId);
        Assert.Equal(call.CallId, start.CallId);
        Assert.Equal("/extensions.Echo/Unary", start.Start.Method);

        var headers = new ExtensionRpcMessage
        {
            SessionId = sessionId,
            ShardId = hello.ShardId,
            CallId = call.CallId,
            Headers = new ExtensionRpcHeaders(),
        };
        await lease.Stream.HandleInboundAsync(headers, CancellationToken.None);

        ExtensionRpcMessage received = await FirstAsync(call.ReadAllAsync(CancellationToken.None));
        Assert.Equal(ExtensionRpcMessage.ContentOneofCase.Headers, received.ContentCase);
    }

    [Fact]
    public async Task Open_RejectsSecondConcurrentPhysicalStream()
    {
        var streamCoordinator = new ExtensionRpcStreamCoordinator();
        await using ExtensionRpcStreamLease lease =
            streamCoordinator.Open(CancellationToken.None);

        Assert.Throws<InvalidOperationException>(() => streamCoordinator.Open(CancellationToken.None));
    }

    [Fact]
    public async Task OpenExtensionCall_AllowsMoreThanPreviousCapacity()
    {
        var streamCoordinator = new ExtensionRpcStreamCoordinator();
        await using ExtensionRpcStreamLease lease =
            streamCoordinator.Open(CancellationToken.None);
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
            Assert.Single(message.Start.Metadata, entry => string.Equals(
                entry.Key, "grpc-timeout", StringComparison.OrdinalIgnoreCase)).Value.ToStringUtf8());
    }

    [Fact]
    public async Task SessionClosed_RemovesStreamFromAssignment()
    {
        var streamCoordinator = new ExtensionRpcStreamCoordinator();
        await using ExtensionRpcStreamLease lease =
            streamCoordinator.Open(CancellationToken.None);
        ExtensionRpcMessage hello = await lease.Stream.Outbound.ReadAsync();
        await lease.Stream.HandleInboundAsync(CreateReady(hello), CancellationToken.None);

        await lease.Stream.HandleInboundAsync(
            new ExtensionRpcMessage
            {
                SessionId = hello.SessionId,
                ShardId = hello.ShardId,
                SessionClosed = new ExtensionRpcSessionClosed(),
            },
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => streamCoordinator.OpenExtensionCallAsync(
            new ExtensionRpcStart { Method = "/extensions.Echo/Unary" },
            CancellationToken.None));
    }

    [Fact]
    public async Task OpenExtensionCall_RejectsInvalidNegotiation()
    {
        var streamCoordinator = new ExtensionRpcStreamCoordinator();
        await using ExtensionRpcStreamLease lease =
            streamCoordinator.Open(CancellationToken.None);
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
    public async Task ExtensionIngress_RelaysOpaqueGrpcFrames()
    {
        var streamCoordinator = new ExtensionRpcStreamCoordinator();
        await using ExtensionRpcStreamLease lease =
            streamCoordinator.Open(CancellationToken.None);
        ExtensionRpcMessage hello = await lease.Stream.Outbound.ReadAsync();
        string sessionId = hello.SessionId;
        await lease.Stream.HandleInboundAsync(CreateReady(hello), CancellationToken.None);

        byte[] requestPayload = Encoding.UTF8.GetBytes("request");
        var context = new DefaultHttpContext();
        context.Connection.LocalPort = 50052;
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/extensions.Echo/Unary";
        context.Request.ContentType = "application/grpc";
        context.Request.Headers["trace-bin"] = "AQI,AwQ=";
        context.Request.Body = new MemoryStream(CreateGrpcFrame(requestPayload));
        context.Response.Body = new MemoryStream();
        var trailersFeature = new TestResponseTrailersFeature();
        context.Features.Set<IHttpResponseTrailersFeature>(trailersFeature);

        var ingress = new ExtensionGrpcIngress(
            new RelayOptions(50051, 50052, 50053, null, "http://localhost:50053", "test-pod"),
            streamCoordinator,
            Metrics,
            NullLogger<ExtensionGrpcIngress>.Instance);

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
            entry => Assert.Equal(ByteString.CopyFrom([3, 4]), entry.Value));
        Assert.Equal(ByteString.CopyFrom(requestPayload), data.Data.Payload);
        Assert.Equal((ulong)requestPayload.Length, data.Data.MessageLength);
        Assert.True(data.Data.EndOfMessage);
        Assert.Equal(ExtensionRpcMessage.ContentOneofCase.HalfClose, halfClose.ContentCase);

        string callId = start.CallId;
        await lease.Stream.HandleInboundAsync(new ExtensionRpcMessage
        {
            SessionId = sessionId,
            ShardId = hello.ShardId,
            CallId = callId,
            Headers = new ExtensionRpcHeaders(),
        }, CancellationToken.None);

        byte[] responsePayload = Encoding.UTF8.GetBytes("response");
        await lease.Stream.HandleInboundAsync(new ExtensionRpcMessage
        {
            SessionId = sessionId,
            ShardId = hello.ShardId,
            CallId = callId,
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
            SessionId = sessionId,
            ShardId = hello.ShardId,
            CallId = callId,
            Complete = new ExtensionRpcComplete
            {
                Status = ExtensionRpcStatus.Ok,
            },
        }, CancellationToken.None);

        await ingressTask;

        Assert.Equal(CreateGrpcFrame(responsePayload), ((MemoryStream)context.Response.Body).ToArray());
        Assert.Equal("0", trailersFeature.Trailers["grpc-status"]);
        Assert.Equal(callId, activity.GetTagItem("azure.functions.worker_proxy.extension_rpc.call_id"));
        Assert.Equal(hello.ShardId, activity.GetTagItem("azure.functions.worker_proxy.extension_rpc.stream_id"));
        Assert.Equal(1, activity.GetTagItem("azure.functions.worker_proxy.extension_rpc.active_calls_at_open"));
        Assert.Equal(1, activity.GetTagItem("azure.functions.worker_proxy.extension_rpc.active_calls_at_completion"));
        Assert.Null(activity.GetTagItem("azure.functions.worker_proxy.extension_rpc.call.open.duration_ms"));
        Assert.Null(activity.GetTagItem("azure.functions.worker_proxy.extension_rpc.call.duration_ms"));
    }

    [Fact]
    public void ExtensionGrpcMetrics_RecordsCallMeasurements()
    {
        var measurements = new ConcurrentQueue<(string Name, double Value)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (string.Equals(
                    instrument.Meter.Name,
                    ExtensionGrpcMetrics.MeterName,
                    StringComparison.Ordinal))
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<double>(
            (instrument, value, _, _) => measurements.Enqueue((instrument.Name, value)));
        listener.SetMeasurementEventCallback<long>(
            (instrument, value, _, _) => measurements.Enqueue((instrument.Name, value)));
        listener.Start();

        Metrics.CallOpenDuration.Record(12.5);
        Metrics.ActiveCalls.Increment();
        Metrics.CallDuration.Record(25.5);
        Metrics.ActiveCalls.Decrement();

        Assert.Contains(
            measurements,
            measurement => string.Equals(
                measurement.Name,
                ExtensionGrpcMetrics.CallOpenDurationInstrumentName,
                StringComparison.Ordinal)
                && measurement.Value == 12.5);
        Assert.Contains(
            measurements,
            measurement => string.Equals(
                measurement.Name,
                ExtensionGrpcMetrics.CallDurationInstrumentName,
                StringComparison.Ordinal)
                && measurement.Value == 25.5);
        Assert.Contains(
            measurements,
            measurement => string.Equals(
                measurement.Name,
                ExtensionGrpcMetrics.ActiveCallsInstrumentName,
                StringComparison.Ordinal)
                && measurement.Value == 1);
        Assert.Contains(
            measurements,
            measurement => string.Equals(
                measurement.Name,
                ExtensionGrpcMetrics.ActiveCallsInstrumentName,
                StringComparison.Ordinal)
                && measurement.Value == -1);
    }

    [Fact]
    public async Task ExtensionIngress_InvalidFrameStopsRelaysBeforeCompletingResponse()
    {
        var streamCoordinator = new ExtensionRpcStreamCoordinator();
        await using ExtensionRpcStreamLease lease =
            streamCoordinator.Open(CancellationToken.None);
        ExtensionRpcMessage hello = await lease.Stream.Outbound.ReadAsync();
        await lease.Stream.HandleInboundAsync(CreateReady(hello), CancellationToken.None);

        byte[] truncatedFrame = new byte[7];
        BinaryPrimitives.WriteUInt32BigEndian(truncatedFrame.AsSpan(1, 4), 10);
        var context = new DefaultHttpContext();
        context.Connection.LocalPort = 50052;
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/extensions.Echo/Unary";
        context.Request.ContentType = "application/grpc";
        context.Request.Body = new MemoryStream(truncatedFrame);
        context.Response.Body = new MemoryStream();

        var ingress = new ExtensionGrpcIngress(
            new RelayOptions(50051, 50052, 50053, null, "http://localhost:50053", "test-pod"),
            streamCoordinator,
            Metrics,
            NullLogger<ExtensionGrpcIngress>.Instance);

        Task ingressTask = ingress.HandleAsync(context);

        ExtensionRpcMessage start = await lease.Stream.Outbound.ReadAsync();
        ExtensionRpcMessage cancel = await lease.Stream.Outbound.ReadAsync();
        await ingressTask;

        Assert.Equal(ExtensionRpcMessage.ContentOneofCase.Start, start.ContentCase);
        Assert.Equal(ExtensionRpcMessage.ContentOneofCase.Cancel, cancel.ContentCase);
        Assert.Equal("13", context.Response.Headers["grpc-status"]);
    }

    [Fact]
    public async Task ExtensionIngress_ResponseExceedingNegotiatedLimitReturnsInternal()
    {
        var streamCoordinator = new ExtensionRpcStreamCoordinator();
        await using ExtensionRpcStreamLease lease =
            streamCoordinator.Open(CancellationToken.None);
        ExtensionRpcMessage hello = await lease.Stream.Outbound.ReadAsync();
        ExtensionRpcMessage ready = CreateReady(hello);
        ready.Ready.MaxDataChunkBytes = 4;
        ready.Ready.MaxMessageBytes = 4;
        await lease.Stream.HandleInboundAsync(ready, CancellationToken.None);

        var context = new DefaultHttpContext();
        context.Connection.LocalPort = 50052;
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/extensions.Echo/Unary";
        context.Request.ContentType = "application/grpc";
        context.Request.Body = new MemoryStream(CreateGrpcFrame([1]));
        context.Response.Body = new MemoryStream();
        var ingress = new ExtensionGrpcIngress(
            new RelayOptions(50051, 50052, 50053, null, "http://localhost:50053", "test-pod"),
            streamCoordinator,
            Metrics,
            NullLogger<ExtensionGrpcIngress>.Instance);

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
    public async Task ExtensionIngress_ResponseChunkExceedingDeclaredLengthWritesNoFrame()
    {
        var streamCoordinator = new ExtensionRpcStreamCoordinator();
        await using ExtensionRpcStreamLease lease = streamCoordinator.Open(CancellationToken.None);
        ExtensionRpcMessage hello = await lease.Stream.Outbound.ReadAsync();
        await lease.Stream.HandleInboundAsync(CreateReady(hello), CancellationToken.None);
        var context = new DefaultHttpContext();
        context.Connection.LocalPort = 50052;
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/extensions.Echo/Unary";
        context.Request.ContentType = "application/grpc";
        context.Request.Body = new MemoryStream(CreateGrpcFrame([1]));
        context.Response.Body = new MemoryStream();
        var ingress = new ExtensionGrpcIngress(
            new RelayOptions(50051, 50052, 50053, null, "http://localhost:50053", "test-pod"),
            streamCoordinator,
            Metrics,
            NullLogger<ExtensionGrpcIngress>.Instance);

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
    public async Task ExtensionIngress_DeadlineExpiresWhileWaitingForNegotiation()
    {
        var streamCoordinator = new ExtensionRpcStreamCoordinator();
        await using ExtensionRpcStreamLease lease = streamCoordinator.Open(CancellationToken.None);
        await lease.Stream.Outbound.ReadAsync();
        var context = new DefaultHttpContext();
        context.Connection.LocalPort = 50052;
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/extensions.Echo/Unary";
        context.Request.ContentType = "application/grpc";
        context.Request.Headers["grpc-timeout"] = "10m";
        context.Request.Body = new MemoryStream();
        context.Response.Body = new MemoryStream();
        var ingress = new ExtensionGrpcIngress(
            new RelayOptions(50051, 50052, 50053, null, "http://localhost:50053", "test-pod"),
            streamCoordinator,
            Metrics,
            NullLogger<ExtensionGrpcIngress>.Instance);

        await ingress.HandleAsync(context);

        Assert.Equal("4", context.Response.Headers["grpc-status"]);
    }

    [Fact]
    public async Task ExtensionIngress_StreamDisconnectReturnsUnavailable()
    {
        var streamCoordinator = new ExtensionRpcStreamCoordinator();
        await using ExtensionRpcStreamLease lease =
            streamCoordinator.Open(CancellationToken.None);
        ExtensionRpcMessage hello = await lease.Stream.Outbound.ReadAsync();
        await lease.Stream.HandleInboundAsync(CreateReady(hello), CancellationToken.None);

        var context = new DefaultHttpContext();
        context.Connection.LocalPort = 50052;
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/extensions.Echo/Unary";
        context.Request.ContentType = "application/grpc";
        context.Request.Body = new MemoryStream(CreateGrpcFrame(Encoding.UTF8.GetBytes("request")));
        context.Response.Body = new MemoryStream();
        var ingress = new ExtensionGrpcIngress(
            new RelayOptions(50051, 50052, 50053, null, "http://localhost:50053", "test-pod"),
            streamCoordinator,
            Metrics,
            NullLogger<ExtensionGrpcIngress>.Instance);

        Task ingressTask = ingress.HandleAsync(context);
        await lease.Stream.Outbound.ReadAsync();
        await lease.Stream.Outbound.ReadAsync();
        await lease.Stream.Outbound.ReadAsync();

        await lease.DisposeAsync();
        await ingressTask;

        Assert.Equal("14", context.Response.Headers["grpc-status"]);
    }

    [Theory]
    [InlineData("1n", 1)]
    [InlineData("99n", 1)]
    [InlineData("100n", 1)]
    [InlineData("101n", 2)]
    [InlineData("1u", 10)]
    public void TryParseTimeout_RoundsPositiveSubTickDurationsUp(string value, long expectedTicks)
    {
        Assert.True(ExtensionGrpcIngress.TryParseTimeout(value, out Google.Protobuf.WellKnownTypes.Duration? timeout));
        Assert.NotNull(timeout);
        Assert.Equal(expectedTicks, timeout.ToTimeSpan().Ticks);
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
    public void CanHandle_ReservesFunctionRpcRoute(string path, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Connection.LocalPort = 50052;
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = path;
        context.Request.ContentType = "application/grpc";
        var ingress = new ExtensionGrpcIngress(
            new RelayOptions(50051, 50052, 50053, null, "http://localhost:50053", "test-pod"),
            new ExtensionRpcStreamCoordinator(),
            Metrics,
            NullLogger<ExtensionGrpcIngress>.Instance);

        Assert.Equal(expected, ingress.CanHandle(context));
    }

    [Theory]
    [InlineData("application/grpc", true)]
    [InlineData("application/grpc+proto", true)]
    [InlineData("application/grpc; charset=utf-8", true)]
    [InlineData("application/grpc-web", false)]
    [InlineData("application/grpcanything", false)]
    public void CanHandle_AcceptsOnlyNativeGrpcContentTypes(string contentType, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Connection.LocalPort = 50052;
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/extensions.Echo/Unary";
        context.Request.ContentType = contentType;
        var ingress = new ExtensionGrpcIngress(
            new RelayOptions(50051, 50052, 50053, null, "http://localhost:50053", "test-pod"),
            new ExtensionRpcStreamCoordinator(),
            Metrics,
            NullLogger<ExtensionGrpcIngress>.Instance);

        Assert.Equal(expected, ingress.CanHandle(context));
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

    private static async Task<T> FirstAsync<T>(IAsyncEnumerable<T> source)
    {
        await foreach (T item in source)
        {
            return item;
        }

        throw new InvalidOperationException("The sequence contained no items.");
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
