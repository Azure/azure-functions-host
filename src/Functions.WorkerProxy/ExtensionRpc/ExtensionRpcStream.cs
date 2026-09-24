// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Azure.Functions.WorkerProxy.ExtensionRpc;

/// <summary>
/// Represents the physical host extension RPC stream that multiplexes logical extension calls.
/// </summary>
internal sealed class ExtensionRpcStream
{
    private readonly ExtensionRpcStreamCoordinator _owner;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly ConcurrentDictionary<string, ExtensionCall> _calls = new();
    private readonly Channel<ExtensionRpcMessage> _outbound = Channel.CreateBounded<ExtensionRpcMessage>(
        new BoundedChannelOptions(256)
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait,
        });

    private volatile ExtensionRpcReady? _ready;
    private int _closed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtensionRpcStream"/> class.
    /// </summary>
    /// <param name="owner">The coordinator that owns this stream.</param>
    /// <param name="sessionId">The identifier shared by reconnects in the current session.</param>
    /// <param name="streamId">The identifier for this physical stream instance.</param>
    /// <param name="cancellationToken">A token that is cancelled when the transport ends.</param>
    public ExtensionRpcStream(
        ExtensionRpcStreamCoordinator owner, string sessionId, string streamId, CancellationToken cancellationToken)
    {
        _owner = owner;
        SessionId = sessionId;
        StreamId = streamId;
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken = _cancellationTokenSource.Token;
    }

    /// <summary>
    /// Gets the extension RPC session identifier.
    /// </summary>
    public string SessionId { get; }

    /// <summary>
    /// Gets the physical stream identifier.
    /// </summary>
    public string StreamId { get; }

    /// <summary>
    /// Gets the token that is cancelled when this stream closes.
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Gets the ordered messages waiting to be written to the host.
    /// </summary>
    public ChannelReader<ExtensionRpcMessage> Outbound => _outbound.Reader;

    /// <summary>
    /// Gets the number of logical calls registered with this stream.
    /// </summary>
    public int ActiveCallCount => _calls.Count;

    /// <summary>
    /// Gets a value indicating whether negotiation completed with the stream enabled.
    /// </summary>
    public bool IsReady => _ready is { Enabled: true };

    /// <summary>
    /// Gets a value indicating whether the host has completed stream negotiation.
    /// </summary>
    public bool IsNegotiated => _ready is not null;

    /// <summary>
    /// Processes a lifecycle message received from the host.
    /// </summary>
    /// <param name="message">The inbound message.</param>
    /// <param name="cancellationToken">A token that cancels message processing.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async ValueTask HandleInboundAsync(ExtensionRpcMessage message, CancellationToken cancellationToken)
    {
        if (!string.Equals(message.SessionId, SessionId, StringComparison.Ordinal)
            || !string.Equals(message.ShardId, StreamId, StringComparison.Ordinal))
        {
            return;
        }

        if (message.ContentCase is ExtensionRpcMessage.ContentOneofCase.SessionClosed)
        {
            _owner.CloseSession(this);
            return;
        }

        if (message.ContentCase is ExtensionRpcMessage.ContentOneofCase.Ready)
        {
            _ready = IsValidReady(message.Ready)
                ? message.Ready
                : new ExtensionRpcReady
                {
                    Enabled = false,
                    RejectionReason = "The host returned invalid extension RPC negotiation settings.",
                };
            _owner.SignalAvailabilityChanged();
            return;
        }

        if (_calls.TryGetValue(message.CallId, out ExtensionCall? call))
        {
            await call.HandleInboundAsync(message, cancellationToken);
        }
    }

    /// <summary>
    /// Registers a logical call and writes its start message.
    /// </summary>
    /// <param name="callId">The identifier assigned to the call.</param>
    /// <param name="start">The call start message.</param>
    /// <param name="cancellationToken">A token that cancels opening the call.</param>
    /// <returns>The registered extension call.</returns>
    public async Task<ExtensionCall> OpenExtensionCallAsync(
        string callId, ExtensionRpcStart start, CancellationToken cancellationToken)
    {
        ExtensionRpcReady ready = _ready
            ?? throw new InvalidOperationException($"Extension RPC stream '{StreamId}' is not ready.");
        if (!ready.Enabled)
        {
            throw new InvalidOperationException(
                $"The host disabled extension RPC stream '{StreamId}': {ready.RejectionReason}");
        }

        ExtensionCall call = new(this, callId, ready);
        if (!_calls.TryAdd(callId, call))
        {
            throw new InvalidOperationException($"Extension call '{callId}' is already registered.");
        }

        bool opened = false;
        try
        {
            await call.WriteAsync(new ExtensionRpcMessage { Start = start }, cancellationToken);
            opened = true;
            return call;
        }
        finally
        {
            if (!opened)
            {
                _calls.TryRemove(callId, out _);
                call.Complete();
            }
        }
    }

    /// <summary>
    /// Closes the stream and completes all logical calls registered with it.
    /// </summary>
    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) is not 0)
        {
            return;
        }

        if (!_cancellationTokenSource.IsCancellationRequested)
        {
            _cancellationTokenSource.Cancel();
        }

        _outbound.Writer.TryComplete();
        foreach (ExtensionCall call in _calls.Values)
        {
            call.Complete();
        }

        _calls.Clear();
        _cancellationTokenSource.Dispose();
    }

    /// <summary>
    /// Adds stream and call correlation identifiers and queues a message for the host.
    /// </summary>
    /// <param name="callId">The logical call identifier.</param>
    /// <param name="message">The message to queue.</param>
    /// <param name="cancellationToken">A token that cancels queueing the message.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    internal async ValueTask WriteExtensionMessageAsync(
        string callId, ExtensionRpcMessage message, CancellationToken cancellationToken)
    {
        message.SessionId = SessionId;
        message.ShardId = StreamId;
        message.CallId = callId;
        await _outbound.Writer.WriteAsync(message, cancellationToken);
    }

    /// <summary>
    /// Removes a completed logical call from this stream.
    /// </summary>
    /// <param name="callId">The identifier of the call to remove.</param>
    internal void RemoveCall(string callId)
    {
        _calls.TryRemove(callId, out _);
    }

    /// <summary>
    /// Queues the initial protocol and transport-capability negotiation message.
    /// </summary>
    internal void QueueHello()
    {
        _outbound.Writer.TryWrite(
            new ExtensionRpcMessage
            {
                SessionId = SessionId,
                ShardId = StreamId,
                Hello = new ExtensionRpcHello
                {
                    InitialReceiveWindowBytes = ExtensionRpcStreamCoordinator.DefaultInitialWindowSize,
                    MaxDataChunkBytes = ExtensionRpcStreamCoordinator.DefaultMaxChunkSize,
                    MaxMessageBytes = ExtensionRpcStreamCoordinator.DefaultMaxMessageSize,
                    SupportedVersions = { ExtensionRpcStreamCoordinator.ProtocolVersion },
                },
            });
    }

    private static bool IsValidReady(ExtensionRpcReady ready)
    {
        return !ready.Enabled || (ready.SelectedVersion == ExtensionRpcStreamCoordinator.ProtocolVersion
            && ready.InitialReceiveWindowBytes is > 0 and <= ExtensionRpcStreamCoordinator.DefaultInitialWindowSize
            && ready.MaxDataChunkBytes is > 0 and <= ExtensionRpcStreamCoordinator.DefaultMaxChunkSize
            && ready.MaxDataChunkBytes <= ready.InitialReceiveWindowBytes
            && ready.MaxMessageBytes is > 0 and <= ExtensionRpcStreamCoordinator.DefaultMaxMessageSize);
    }
}
