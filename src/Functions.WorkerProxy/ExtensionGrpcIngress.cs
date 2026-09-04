using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Azure.Functions.WorkerProxy;

/// <summary>
/// Accepts arbitrary worker-facing gRPC requests and relays them over the host extension RPC stream.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ExtensionGrpcIngress"/> class.
/// </remarks>
/// <param name="options">The configured worker and runtime relay endpoints.</param>
/// <param name="streamCoordinator">The extension RPC stream coordinator.</param>
/// <param name="metrics">The extension gRPC metrics.</param>
/// <param name="logger">The logger used for ingress diagnostics.</param>
internal sealed partial class ExtensionGrpcIngress(
    RelayOptions options,
    ExtensionRpcStreamCoordinator streamCoordinator,
    ExtensionGrpcMetrics metrics,
    ILogger<ExtensionGrpcIngress> logger)
{
    internal const string FunctionRpcEventStreamPath = "/AzureFunctionsRpcMessages.FunctionRpc/EventStream";
    internal const string ExtensionRpcEventStreamPath = "/AzureFunctionsExtensionRpcMessages.ExtensionRpc/EventStream";

    private const string GrpcContentType = "application/grpc";
    private const string GrpcStatusHeader = "grpc-status";
    private const string GrpcMessageHeader = "grpc-message";
    private const long ProtobufDurationMaxSeconds = 315_576_000_000;
    private static readonly TimeSpan CancellationWriteTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaxCancellationTimerDuration =
        TimeSpan.FromMilliseconds(uint.MaxValue - 1);
    private static readonly HashSet<string> ExcludedRequestHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        // The bridge creates the runtime-side gRPC request with its own application/grpc content type.
        "content-type",

        // Host identifies the worker-to-proxy hop and must not become metadata for the reconstructed request.
        "host",

        // TE advertises HTTP trailer support for the current hop; the bridge handles response trailers explicitly.
        "te",
    };

    private readonly RelayOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly ExtensionRpcStreamCoordinator _streamCoordinator = streamCoordinator
        ?? throw new ArgumentNullException(nameof(streamCoordinator));
    private readonly ExtensionGrpcMetrics _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Determines whether the request is a worker-facing extension gRPC call.
    /// </summary>
    /// <param name="context">The HTTP context to inspect.</param>
    /// <returns><see langword="true"/> when this ingress should handle the request.</returns>
    public bool CanHandle(HttpContext context)
    {
        return context.Connection.LocalPort == _options.WorkerGrpcPort
            && HttpMethods.IsPost(context.Request.Method)
            && IsGrpcContentType(context.Request.ContentType)
            && !string.Equals(context.Request.Path.Value, FunctionRpcEventStreamPath, StringComparison.Ordinal)
            && !string.Equals(context.Request.Path.Value, ExtensionRpcEventStreamPath, StringComparison.Ordinal);
    }

    private static bool IsGrpcContentType(string? contentType)
    {
        if (contentType is null)
        {
            return false;
        }

        int parameterIndex = contentType.IndexOf(';', StringComparison.Ordinal);
        ReadOnlySpan<char> mediaType = parameterIndex >= 0
            ? contentType.AsSpan(0, parameterIndex).Trim()
            : contentType.AsSpan().Trim();

        return mediaType.Equals(GrpcContentType, StringComparison.OrdinalIgnoreCase)
            || (mediaType.StartsWith($"{GrpcContentType}+", StringComparison.OrdinalIgnoreCase)
                && mediaType.Length > GrpcContentType.Length + 1);
    }

    /// <summary>
    /// Relays a worker-facing gRPC request through the connected host extension RPC stream.
    /// </summary>
    /// <param name="context">The HTTP context for the worker-facing call.</param>
    /// <returns>A task that represents the asynchronous relay.</returns>
    public async Task HandleAsync(HttpContext context)
    {
        if (!_streamCoordinator.HasConnectedStream)
        {
            await CompleteWithStatusAsync(
                context.Response, ExtensionRpcStatus.Unavailable, "The host runtime RPC stream is not connected.");

            return;
        }

        ExtensionRpcStart start;
        try
        {
            start = CreateStart(context.Request);
        }
        catch (FormatException exception)
        {
            await CompleteWithStatusAsync(context.Response, ExtensionRpcStatus.InvalidArgument, exception.Message);
            return;
        }

        using CancellationTokenSource requestLifetimeCancellationTokenSource =
            CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        TimeSpan? timeout = start.Timeout?.ToTimeSpan();
        if (timeout is not null)
        {
            if (timeout <= TimeSpan.Zero)
            {
                requestLifetimeCancellationTokenSource.Cancel();
            }
            else if (timeout <= MaxCancellationTimerDuration)
            {
                requestLifetimeCancellationTokenSource.CancelAfter(timeout.Value);
            }
        }

        CancellationToken requestLifetimeToken = requestLifetimeCancellationTokenSource.Token;
        long callStart = Stopwatch.GetTimestamp();
        ExtensionCall call;
        try
        {
            call = await _streamCoordinator.OpenExtensionCallAsync(start, requestLifetimeToken);
        }
        catch (InvalidOperationException exception)
        {
            await CompleteWithStatusAsync(context.Response, ExtensionRpcStatus.Unavailable, exception.Message);
            return;
        }
        catch (OperationCanceledException) when (requestLifetimeToken.IsCancellationRequested)
        {
            if (!context.RequestAborted.IsCancellationRequested)
            {
                await CompleteWithStatusAsync(
                    context.Response,
                    ExtensionRpcStatus.DeadlineExceeded,
                    "The extension gRPC call deadline was exceeded.");
            }

            return;
        }

        int activeCallCountAtOpen = call.ActiveCallCount;
        double openDurationMilliseconds = Stopwatch.GetElapsedTime(callStart).TotalMilliseconds;
        ExtensionGrpcActivity.CallOpened(call.CallId, call.StreamId, activeCallCountAtOpen);
        _metrics.CallOpenDuration.Record(openDurationMilliseconds);
        _metrics.ActiveCalls.Increment();

        // Remove this per-call log when WorkerProxy metric exporting is wired up.
        Log.CallOpened(_logger, start.Method, call.CallId, activeCallCountAtOpen, openDurationMilliseconds);
        await using ExtensionCall callScope = call;
        using CancellationTokenSource cancellationTokenSource =
            CancellationTokenSource.CreateLinkedTokenSource(requestLifetimeToken, call.CancellationToken);

        Task requestTask = RelayRequestAsync(context.Request.Body, call, cancellationTokenSource.Token);
        Task responseTask = RelayResponseAsync(context.Response, call, cancellationTokenSource.Token);

        try
        {
            Task completedTask = await Task.WhenAny(requestTask, responseTask);
            await completedTask;
            if (ReferenceEquals(completedTask, requestTask))
            {
                await responseTask;
            }
        }
        catch (InvalidDataException exception)
        {
            await StopRelayTasksAsync(cancellationTokenSource, requestTask, responseTask);
            Log.InvalidFraming(_logger, exception, start.Method);
            await TryCancelHostCallAsync(call, "The extension gRPC bridge rejected invalid framing.");
            await CompleteWithStatusAsync(context.Response, ExtensionRpcStatus.Internal, exception.Message);
        }
        catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
        {
            await StopRelayTasksAsync(cancellationTokenSource, requestTask, responseTask);
            if (!call.CancellationToken.IsCancellationRequested)
            {
                bool deadlineExceeded = !context.RequestAborted.IsCancellationRequested
                    && requestLifetimeToken.IsCancellationRequested;
                string detail = deadlineExceeded
                    ? "The extension gRPC call deadline was exceeded."
                    : "The worker-facing gRPC request was cancelled.";
                await TryCancelHostCallAsync(call, detail);
                if (deadlineExceeded)
                {
                    await CompleteWithStatusAsync(
                        context.Response, ExtensionRpcStatus.DeadlineExceeded, detail);
                }
            }
            else if (!context.RequestAborted.IsCancellationRequested)
            {
                await CompleteWithStatusAsync(
                    context.Response, ExtensionRpcStatus.Unavailable, "The extension RPC stream disconnected.");
            }
        }
        finally
        {
            await StopRelayTasksAsync(cancellationTokenSource, requestTask, responseTask);
            int activeCallCountAtCompletion = call.ActiveCallCount;
            double callDurationMilliseconds = Stopwatch.GetElapsedTime(callStart).TotalMilliseconds;
            ExtensionGrpcActivity.CallCompleted(activeCallCountAtCompletion);
            _metrics.CallDuration.Record(callDurationMilliseconds);
            _metrics.ActiveCalls.Decrement();

            // Remove this per-call log when WorkerProxy metric exporting is wired up.
            Log.CallCompleted(
                _logger,
                start.Method,
                call.CallId,
                activeCallCountAtOpen,
                activeCallCountAtCompletion,
                callDurationMilliseconds);
        }
    }

    private static ExtensionRpcStart CreateStart(HttpRequest request)
    {
        ExtensionRpcStart start = new()
        {
            Method = request.Path.Value ?? string.Empty,
        };

        foreach ((string key, StringValues values) in request.Headers)
        {
            if (ExcludedRequestHeaders.Contains(key))
            {
                continue;
            }

            foreach (string? value in values)
            {
                if (value is null)
                {
                    continue;
                }

                string[] metadataValues = key.EndsWith("-bin", StringComparison.OrdinalIgnoreCase)
                    ? value.Split(',', StringSplitOptions.TrimEntries) : [value];
                foreach (string metadataValue in metadataValues)
                {
                    start.Metadata.Add(new ExtensionRpcMetadataEntry
                    {
                        Key = key,
                        Value = ToMetadataBytes(key, metadataValue),
                    });
                }
            }
        }

        if (request.Headers.TryGetValue("grpc-timeout", out StringValues timeoutValues)
            && TryParseTimeout(timeoutValues.ToString(), out Duration? timeout))
        {
            start.Timeout = timeout;
        }

        return start;
    }

    private static async Task RelayRequestAsync(
        Stream requestBody, ExtensionCall call, CancellationToken cancellationToken)
    {
        byte[] prefix = new byte[5];
        ulong messageId = 0;

        while (await TryReadExactlyAsync(requestBody, prefix, cancellationToken))
        {
            bool compressed = prefix[0] is 1;
            if (prefix[0] is not 0 and not 1)
            {
                throw new InvalidDataException($"Invalid gRPC compression flag '{prefix[0]}'.");
            }

            uint messageLength = BinaryPrimitives.ReadUInt32BigEndian(prefix.AsSpan(1));
            if (messageLength > call.Ready.MaxMessageBytes)
            {
                throw new InvalidDataException(
                    $"The gRPC message length '{messageLength}' exceeds the negotiated limit '{call.Ready.MaxMessageBytes}'.");
            }

            messageId++;
            uint chunkSize = (uint)Math.Max(
                1, Math.Min(call.Ready.MaxDataChunkBytes, call.Ready.InitialReceiveWindowBytes));
            byte[] buffer = new byte[Math.Min(messageLength, chunkSize)];
            ulong offset = 0;

            if (messageLength is 0)
            {
                await WriteDataAsync(
                    call,
                    messageId,
                    offset,
                    messageLength,
                    compressed,
                    ByteString.Empty,
                    true,
                    cancellationToken);
                continue;
            }

            while (offset < messageLength)
            {
                int count = (int)Math.Min((ulong)buffer.Length, messageLength - offset);
                await ReadExactlyAsync(requestBody, buffer.AsMemory(0, count), cancellationToken);
                offset += (uint)count;

                await WriteDataAsync(
                    call,
                    messageId,
                    offset - (uint)count,
                    messageLength,
                    compressed,
                    ByteString.CopyFrom(buffer, 0, count),
                    offset == messageLength,
                    cancellationToken);
            }
        }

        await call.WriteAsync(
            new ExtensionRpcMessage { HalfClose = new ExtensionRpcHalfClose(), },
            cancellationToken);
    }

    private static async Task RelayResponseAsync(
        HttpResponse response,
        ExtensionCall call,
        CancellationToken cancellationToken)
    {
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = GrpcContentType;

        ulong? messageId = null;
        ulong messageLength = 0;
        ulong nextOffset = 0;

        await foreach (ExtensionRpcMessage message in call.ReadAllAsync(cancellationToken))
        {
            switch (message.ContentCase)
            {
                case ExtensionRpcMessage.ContentOneofCase.Headers:
                    AddMetadata(response.Headers, message.Headers.Metadata);
                    break;
                case ExtensionRpcMessage.ContentOneofCase.Data:
                    ExtensionRpcData data = message.Data;
                    if (data.MessageLength > call.Ready.MaxMessageBytes
                        || data.Payload.Length > call.Ready.MaxDataChunkBytes)
                    {
                        throw new InvalidDataException("The extension response exceeded negotiated transport limits.");
                    }

                    bool startsMessage = data.Offset is 0;
                    if (startsMessage)
                    {
                        if (messageId is not null)
                        {
                            throw new InvalidDataException("A response message started before the previous message ended.");
                        }

                        messageId = data.MessageId;
                        messageLength = data.MessageLength;
                        nextOffset = 0;
                    }

                    if (messageId != data.MessageId || data.Offset != nextOffset)
                    {
                        throw new InvalidDataException(
                            $"Unexpected extension response chunk offset '{data.Offset}' for message '{data.MessageId}'.");
                    }

                    ulong remainingLength = messageLength - nextOffset;
                    if ((ulong)data.Payload.Length > remainingLength
                        || (data.EndOfMessage && (ulong)data.Payload.Length != remainingLength)
                        || (!data.EndOfMessage && (ulong)data.Payload.Length >= remainingLength))
                    {
                        throw new InvalidDataException(
                            $"Extension response chunk for message '{data.MessageId}' exceeds its declared length.");
                    }

                    if (startsMessage)
                    {
                        await WriteGrpcPrefixAsync(response.Body, data.Compressed, messageLength, cancellationToken);
                    }

                    await response.Body.WriteAsync(data.Payload.Memory, cancellationToken);
                    await call.WriteAsync(
                        new ExtensionRpcMessage
                        {
                            WindowUpdate = new ExtensionRpcWindowUpdate
                            {
                                ByteCount = (ulong)data.Payload.Length,
                            },
                        },
                        cancellationToken);
                    nextOffset += (ulong)data.Payload.Length;
                    if (data.EndOfMessage)
                    {
                        if (nextOffset != messageLength)
                        {
                            throw new InvalidDataException(
                                $"Extension response message '{data.MessageId}' ended at '{nextOffset}' bytes; "
                                + $"expected '{messageLength}'.");
                        }

                        messageId = null;
                    }

                    break;
                case ExtensionRpcMessage.ContentOneofCase.Complete:
                    if (messageId is not null)
                    {
                        throw new InvalidDataException("The extension response completed during a message.");
                    }

                    AddTrailers(response, message.Complete.Trailers);
                    response.AppendTrailer(GrpcStatusHeader, ((int)message.Complete.Status).ToStringInvariant());
                    if (!string.IsNullOrEmpty(message.Complete.Detail))
                    {
                        response.AppendTrailer(GrpcMessageHeader, Uri.EscapeDataString(message.Complete.Detail));
                    }

                    await response.Body.FlushAsync(cancellationToken);

                    return;
            }
        }

        throw new InvalidDataException("The runtime RPC stream closed before the extension call completed.");
    }

    private static async ValueTask WriteDataAsync(
        ExtensionCall call,
        ulong messageId,
        ulong offset,
        ulong messageLength,
        bool compressed,
        ByteString payload,
        bool endOfMessage,
        CancellationToken cancellationToken)
    {
        await call.WriteAsync(
            new ExtensionRpcMessage
            {
                Data = new ExtensionRpcData
                {
                    MessageId = messageId,
                    Offset = offset,
                    MessageLength = messageLength,
                    Compressed = compressed,
                    Payload = payload,
                    EndOfMessage = endOfMessage,
                },
            },
            cancellationToken);
    }

    private static async Task WriteGrpcPrefixAsync(
        Stream responseBody, bool compressed, ulong messageLength, CancellationToken cancellationToken)
    {
        if (messageLength > uint.MaxValue)
        {
            throw new InvalidDataException(
                $"Extension response message length '{messageLength}' exceeds the gRPC framing limit.");
        }

        byte[] prefix = new byte[5];
        prefix[0] = compressed ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32BigEndian(prefix.AsSpan(1), (uint)messageLength);
        await responseBody.WriteAsync(prefix, cancellationToken);
    }

    private static void AddMetadata(IHeaderDictionary headers, IEnumerable<ExtensionRpcMetadataEntry> metadata)
    {
        foreach (ExtensionRpcMetadataEntry entry in metadata)
        {
            string value = entry.Key.EndsWith("-bin", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToBase64String(entry.Value.Span)
                : entry.Value.ToStringUtf8();
            headers.Append(entry.Key, value);
        }
    }

    private static void AddTrailers(HttpResponse response, IEnumerable<ExtensionRpcMetadataEntry> metadata)
    {
        foreach (ExtensionRpcMetadataEntry entry in metadata)
        {
            string value = entry.Key.EndsWith("-bin", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToBase64String(entry.Value.Span)
                : entry.Value.ToStringUtf8();
            response.AppendTrailer(entry.Key, value);
        }
    }

    private static ByteString ToMetadataBytes(string key, string value)
    {
        return key.EndsWith("-bin", StringComparison.OrdinalIgnoreCase)
            ? ByteString.CopyFrom(DecodeBinaryMetadata(value))
            : ByteString.CopyFrom(value, Encoding.UTF8);
    }

    private static byte[] DecodeBinaryMetadata(string value)
    {
        int remainder = value.Length % 4;
        if (remainder is 1)
        {
            throw new FormatException("Binary gRPC metadata contains invalid base64.");
        }

        return Convert.FromBase64String(remainder is 0 ? value : value.PadRight(value.Length + 4 - remainder, '='));
    }

    /// <summary>
    /// Parses a gRPC timeout header into a protobuf duration.
    /// </summary>
    /// <param name="value">The timeout header value.</param>
    /// <param name="timeout">The parsed duration when parsing succeeds.</param>
    /// <returns><see langword="true"/> when the value is a valid gRPC timeout.</returns>
    internal static bool TryParseTimeout(string value, out Duration? timeout)
    {
        timeout = null;
        if (value.Length is < 2 or > 9
            || !long.TryParse(
                value.AsSpan(0, value.Length - 1),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out long amount))
        {
            return false;
        }

        long ticks;
        try
        {
            ticks = value[^1] switch
            {
                'H' => checked(amount * TimeSpan.TicksPerHour),
                'M' => checked(amount * TimeSpan.TicksPerMinute),
                'S' => checked(amount * TimeSpan.TicksPerSecond),
                'm' => checked(amount * TimeSpan.TicksPerMillisecond),
                'u' => checked(amount * 10),
                'n' => amount is 0 ? 0 : checked((amount + 99) / 100),
                _ => -1,
            };
        }
        catch (OverflowException)
        {
            return false;
        }

        if (ticks < 0 || ticks > ProtobufDurationMaxSeconds * TimeSpan.TicksPerSecond)
        {
            return false;
        }

        timeout = Duration.FromTimeSpan(TimeSpan.FromTicks(ticks));

        return true;
    }

    private static async Task TryCancelHostCallAsync(ExtensionCall call, string detail)
    {
        using CancellationTokenSource cancellationTokenSource =
            CancellationTokenSource.CreateLinkedTokenSource(call.CancellationToken);
        cancellationTokenSource.CancelAfter(CancellationWriteTimeout);
        try
        {
            await call.WriteAsync(
                new ExtensionRpcMessage
                {
                    Cancel = new ExtensionRpcCancel
                    {
                        Detail = detail,
                    },
                },
                cancellationTokenSource.Token);
        }
        catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
        {
        }
        catch (System.Threading.Channels.ChannelClosedException)
        {
        }
    }

    private static async Task StopRelayTasksAsync(
        CancellationTokenSource cancellationTokenSource, Task requestTask, Task responseTask)
    {
        if (!cancellationTokenSource.IsCancellationRequested)
        {
            await cancellationTokenSource.CancelAsync();
        }

        await Task.WhenAll(ObserveRelayTaskAsync(requestTask), ObserveRelayTaskAsync(responseTask));
    }

    private static async Task ObserveRelayTaskAsync(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
            // The initiating relay failure is handled by the caller after both pumps stop.
        }
    }

    private static async Task<bool> TryReadExactlyAsync(
        Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int count = await stream.ReadAsync(buffer[offset..], cancellationToken);
            if (count is 0)
            {
                if (offset is 0)
                {
                    return false;
                }

                throw new InvalidDataException("The gRPC message prefix ended unexpectedly.");
            }

            offset += count;
        }

        return true;
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (!await TryReadExactlyAsync(stream, buffer, cancellationToken))
        {
            throw new InvalidDataException("The gRPC message payload ended unexpectedly.");
        }
    }

    private static async Task CompleteWithStatusAsync(HttpResponse response, ExtensionRpcStatus status, string detail)
    {
        string statusValue = ((int)status).ToStringInvariant();
        string messageValue = Uri.EscapeDataString(detail);
        if (response.HasStarted)
        {
            response.AppendTrailer(GrpcStatusHeader, statusValue);
            response.AppendTrailer(GrpcMessageHeader, messageValue);
        }
        else
        {
            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = GrpcContentType;
            response.Headers[GrpcStatusHeader] = statusValue;
            response.Headers[GrpcMessageHeader] = messageValue;
        }

        await response.CompleteAsync();
    }

    private static partial class Log
    {
        [LoggerMessage(
            LogLevel.Information,
            "Extension gRPC call opened. Method: {Method}, CallId: {CallId}, "
            + "ActiveCallCount: {ActiveCallCount}, OpenElapsedMilliseconds: {OpenElapsedMilliseconds}.")]
        public static partial void CallOpened(
            ILogger logger,
            string method,
            string callId,
            int activeCallCount,
            double openElapsedMilliseconds);

        [LoggerMessage(
            LogLevel.Information,
            "Extension gRPC call completed. Method: {Method}, CallId: {CallId}, "
            + "ActiveCallCountAtOpen: {ActiveCallCountAtOpen}, "
            + "ActiveCallCountAtCompletion: {ActiveCallCountAtCompletion}, "
            + "ElapsedMilliseconds: {ElapsedMilliseconds}.")]
        public static partial void CallCompleted(
            ILogger logger,
            string method,
            string callId,
            int activeCallCountAtOpen,
            int activeCallCountAtCompletion,
            double elapsedMilliseconds);

        [LoggerMessage(LogLevel.Warning, "Invalid extension gRPC framing for {Method}.")]
        public static partial void InvalidFraming(ILogger logger, Exception exception, string method);
    }
}
