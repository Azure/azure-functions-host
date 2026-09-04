// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;

/// <summary>
/// Reconstructs extension RPC lifecycle messages as ASP.NET Core gRPC endpoint invocations.
/// </summary>
internal sealed partial class ExtensionRpcStreamDispatcher : IAsyncDisposable
{
    internal const uint ProtocolVersion = 1;
    internal const uint DefaultMaxChunkSize = 64 * 1024;
    internal const ulong DefaultInitialWindowSize = 1024 * 1024;
    internal const ulong DefaultMaxMessageSize = 16 * 1024 * 1024;
    internal static readonly TimeSpan TerminalWriteTimeout = TimeSpan.FromMilliseconds(250);

    private const string GrpcContentType = "application/grpc";
    private static readonly TimeSpan MaxCancellationTimerDuration =
        TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    private readonly string _workerId;
    private readonly IExtensionRpcEndpointRouter _endpointRouter;
    private readonly ChannelWriter<ExtensionRpcMessage> _outbound;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, DispatchCall> _calls = new();
    private readonly Lock _syncLock = new();
    private CancellationTokenSource? _sessionCancellationTokenSource;
    private string? _sessionId;
    private string? _shardId;
    private ulong _initialResponseWindow;
    private uint _maxDataChunkSize;
    private ulong _maxMessageSize;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtensionRpcStreamDispatcher"/> class.
    /// </summary>
    /// <param name="workerId">The worker identifier associated with the stream.</param>
    /// <param name="endpointRouter">The router for registered extension endpoints.</param>
    /// <param name="outbound">The writer for lifecycle messages sent to the proxy.</param>
    /// <param name="logger">The logger used for dispatch diagnostics.</param>
    public ExtensionRpcStreamDispatcher(
        string workerId,
        IExtensionRpcEndpointRouter endpointRouter,
        ChannelWriter<ExtensionRpcMessage> outbound,
        ILogger logger)
    {
        _workerId = workerId ?? throw new ArgumentNullException(nameof(workerId));
        _endpointRouter = endpointRouter ?? throw new ArgumentNullException(nameof(endpointRouter));
        _outbound = outbound ?? throw new ArgumentNullException(nameof(outbound));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the number of logical calls currently dispatched on the stream.
    /// </summary>
    internal int ActiveCallCount => _calls.Count;

    /// <summary>
    /// Handles the next lifecycle message received from the proxy.
    /// </summary>
    /// <param name="message">The lifecycle message to process.</param>
    /// <param name="cancellationToken">A token that cancels message processing.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async ValueTask HandleAsync(ExtensionRpcMessage message, CancellationToken cancellationToken)
    {
        switch (message.ContentCase)
        {
            case ExtensionRpcMessage.ContentOneofCase.Hello:
                await StartSessionAsync(message, cancellationToken);
                break;
            case ExtensionRpcMessage.ContentOneofCase.SessionClosed:
                await CloseSessionAsync(message.SessionId);
                break;
            default:
                if (!IsActiveStream(message.SessionId, message.ShardId))
                {
                    return;
                }

                if (message.ContentCase is ExtensionRpcMessage.ContentOneofCase.Start)
                {
                    StartCall(message);
                }
                else if (_calls.TryGetValue(message.CallId, out DispatchCall? call))
                {
                    await call.QueueInboundAsync(message, cancellationToken);
                }

                break;
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await CloseSessionAsync(_sessionId);
    }

    private async ValueTask StartSessionAsync(ExtensionRpcMessage message, CancellationToken cancellationToken)
    {
        await CloseSessionAsync(_sessionId);

        bool supported = message.Hello.SupportedVersions.Contains(ProtocolVersion)
            && message.Hello.InitialReceiveWindowBytes > 0
            && message.Hello.MaxDataChunkBytes > 0
            && message.Hello.MaxMessageBytes > 0;
        ulong initialResponseWindow = Math.Min(message.Hello.InitialReceiveWindowBytes, DefaultInitialWindowSize);
        uint maxDataChunkSize = Math.Min(message.Hello.MaxDataChunkBytes, DefaultMaxChunkSize);
        maxDataChunkSize = (uint)Math.Min(maxDataChunkSize, initialResponseWindow);
        ulong maxMessageSize = Math.Min(message.Hello.MaxMessageBytes, DefaultMaxMessageSize);
        lock (_syncLock)
        {
            _sessionId = message.SessionId;
            _shardId = message.ShardId;
            _sessionCancellationTokenSource = new CancellationTokenSource();
            _initialResponseWindow = initialResponseWindow;
            _maxDataChunkSize = maxDataChunkSize;
            _maxMessageSize = maxMessageSize;
        }

        await WriteAsync(
            new ExtensionRpcMessage
            {
                SessionId = message.SessionId,
                ShardId = message.ShardId,
                Ready = new ExtensionRpcReady
                {
                    SelectedVersion = supported ? ProtocolVersion : 0,
                    Enabled = supported,
                    RejectionReason = supported
                        ? string.Empty
                        : "The extension RPC version or transport limits are not supported.",
                    InitialReceiveWindowBytes = initialResponseWindow,
                    MaxDataChunkBytes = maxDataChunkSize,
                    MaxMessageBytes = maxMessageSize,
                },
            },
            cancellationToken);
    }

    private void StartCall(ExtensionRpcMessage message)
    {
        CancellationToken sessionCancellationToken;
        ulong initialResponseWindow;
        uint maxDataChunkSize;
        ulong maxMessageSize;
        lock (_syncLock)
        {
            if (_sessionCancellationTokenSource is null)
            {
                return;
            }

            sessionCancellationToken = _sessionCancellationTokenSource.Token;
            initialResponseWindow = _initialResponseWindow;
            maxDataChunkSize = _maxDataChunkSize;
            maxMessageSize = _maxMessageSize;
        }

        var call = new DispatchCall(
            _workerId,
            message.SessionId,
            message.ShardId,
            message.CallId,
            message.Start,
            _endpointRouter,
            _outbound,
            _logger,
            sessionCancellationToken,
            initialResponseWindow,
            maxDataChunkSize,
            maxMessageSize,
            () => _calls.TryRemove(message.CallId, out _));

        if (_calls.TryAdd(message.CallId, call))
        {
            call.Start();
        }
    }

    private bool IsActiveStream(string sessionId, string streamId)
    {
        lock (_syncLock)
        {
            return string.Equals(_sessionId, sessionId, StringComparison.Ordinal)
                && string.Equals(_shardId, streamId, StringComparison.Ordinal);
        }
    }

    private async ValueTask CloseSessionAsync(string? sessionId)
    {
        CancellationTokenSource? cancellationTokenSource;
        DispatchCall[] calls;
        lock (_syncLock)
        {
            if (sessionId is not null && !string.Equals(_sessionId, sessionId, StringComparison.Ordinal))
            {
                return;
            }

            _sessionId = null;
            _shardId = null;
            cancellationTokenSource = _sessionCancellationTokenSource;
            _sessionCancellationTokenSource = null;
            calls = [.. _calls.Values];
            _calls.Clear();
        }

        if (cancellationTokenSource is not null)
        {
            await cancellationTokenSource.CancelAsync();
            cancellationTokenSource.Dispose();
        }

        foreach (DispatchCall call in calls)
        {
            call.Cancel();
            _ = ObserveCallCompletionAsync(call.Completion);
        }
    }

    private async ValueTask WriteAsync(ExtensionRpcMessage message, CancellationToken cancellationToken)
    {
        await _outbound.WriteAsync(message, cancellationToken);
    }

    private async Task ObserveCallCompletionAsync(Task completion)
    {
        try
        {
            await completion;
        }
        catch (Exception exception)
        {
            Log.CallStoppedAfterStreamClosed(_logger, exception);
        }
    }
}
