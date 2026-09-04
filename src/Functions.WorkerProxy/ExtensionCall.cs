// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Channels;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Microsoft.Azure.Functions.WorkerProxy;

/// <summary>
/// Represents one worker-originated gRPC call multiplexed over the shared extension RPC stream.
/// </summary>
/// <param name="stream">The physical extension RPC stream carrying the call.</param>
/// <param name="callId">The identifier used to correlate lifecycle messages for the call.</param>
/// <param name="ready">The negotiated transport settings for the stream.</param>
internal sealed class ExtensionCall(ExtensionRpcStream stream, string callId, ExtensionRpcReady ready)
    : IAsyncDisposable
{
    // This modest per-call buffer absorbs short scheduling bursts between the shared stream reader and the
    // worker-facing response relay. Keeping it small bounds aggregate buffering across all concurrent calls;
    // payload size and bytes in flight are bounded separately by negotiated chunk and flow-control limits.
    private const int InboundQueueCapacity = 32;

    private readonly Channel<ExtensionRpcMessage> _inbound = Channel.CreateBounded<ExtensionRpcMessage>(
        new BoundedChannelOptions(InboundQueueCapacity)
        {
            // Each call has one worker-facing response relay consuming its ordered response messages.
            SingleReader = true,

            // The single physical extension stream reader is the only source of inbound messages.
            SingleWriter = true,

            // Do not run a call's response relay inline on the shared stream reader. A slow consumer must not
            // delay demultiplexing unrelated calls through a synchronous continuation.
            AllowSynchronousContinuations = false,

            // Dropping any data or lifecycle message would corrupt the logical gRPC call. Waiting applies
            // backpressure until the worker-facing response relay catches up.
            FullMode = BoundedChannelFullMode.Wait,
        });

    // Request data consumes the receive-window credits granted by the host. Control messages do not carry
    // payload bytes and therefore do not consume this window.
    private readonly ExtensionRpcCreditWindow _requestCredits = new(ready.InitialReceiveWindowBytes);
    private int _disposed;

    /// <summary>
    /// Gets the identifier used to correlate lifecycle messages for this call.
    /// </summary>
    public string CallId { get; } = callId;

    /// <summary>
    /// Gets the identifier of the physical extension RPC stream carrying this call.
    /// </summary>
    public string StreamId => stream.StreamId;

    /// <summary>
    /// Gets the transport settings negotiated for the stream.
    /// </summary>
    public ExtensionRpcReady Ready { get; } = ready;

    /// <summary>
    /// Gets the token that is cancelled when the physical stream closes.
    /// </summary>
    public CancellationToken CancellationToken => stream.CancellationToken;

    /// <summary>
    /// Gets a snapshot of the logical calls currently registered with the physical stream.
    /// </summary>
    public int ActiveCallCount => stream.ActiveCallCount;

    /// <summary>
    /// Reads ordered response lifecycle messages received from the host.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels response enumeration.</param>
    /// <returns>The ordered response messages for this call.</returns>
    public IAsyncEnumerable<ExtensionRpcMessage> ReadAllAsync(CancellationToken cancellationToken) =>
        _inbound.Reader.ReadAllAsync(cancellationToken);

    /// <summary>
    /// Writes a request lifecycle message to the shared extension RPC stream.
    /// </summary>
    /// <param name="message">The message to write.</param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>A task that represents the asynchronous write.</returns>
    public async ValueTask WriteAsync(ExtensionRpcMessage message, CancellationToken cancellationToken)
    {
        if (message.ContentCase is ExtensionRpcMessage.ContentOneofCase.Data)
        {
            await _requestCredits.ReserveAsync((ulong)message.Data.Payload.Length, cancellationToken);
        }

        await stream.WriteExtensionMessageAsync(CallId, message, cancellationToken);
    }

    /// <summary>
    /// Processes a response lifecycle message routed from the shared extension RPC stream.
    /// </summary>
    /// <param name="message">The inbound message to process.</param>
    /// <param name="cancellationToken">A token that cancels queueing the message.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async ValueTask HandleInboundAsync(
        ExtensionRpcMessage message,
        CancellationToken cancellationToken)
    {
        if (message.ContentCase is ExtensionRpcMessage.ContentOneofCase.WindowUpdate)
        {
            // Flow-control messages are transport bookkeeping for request writes, not response messages for
            // the worker-facing call.
            _requestCredits.Add(message.WindowUpdate.ByteCount);
            return;
        }

        await _inbound.Writer.WriteAsync(message, cancellationToken);
        if (message.ContentCase is ExtensionRpcMessage.ContentOneofCase.Complete)
        {
            // Complete the channel only after publishing the terminal message so the reader observes status
            // and trailers before its enumeration ends.
            _inbound.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Completes the inbound response queue because its physical stream has ended.
    /// </summary>
    public void Complete() => _inbound.Writer.TryComplete();

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is 0)
        {
            // Removing the call prevents later frames from being routed to a worker request that has ended.
            stream.RemoveCall(CallId);
            _inbound.Writer.TryComplete();
        }

        return ValueTask.CompletedTask;
    }
}
