// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Azure.Functions.WorkerProxy;

/// <summary>
/// Represents one worker-originated gRPC call multiplexed over the shared extension RPC stream.
/// </summary>
/// <param name="stream">The physical extension RPC stream carrying the call.</param>
/// <param name="callId">The identifier used to correlate lifecycle messages for the call.</param>
/// <param name="ready">The negotiated transport settings for the stream.</param>
internal sealed class ExtensionCall(ExtensionRpcStream stream, string callId, ExtensionRpcReady ready)
    : IAsyncDisposable
{
    private const int InboundQueueCapacity = 32;

    private readonly Channel<ExtensionRpcMessage> _inbound = Channel.CreateBounded<ExtensionRpcMessage>(
        new BoundedChannelOptions(InboundQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait,
        });

    private readonly ExtensionRpcCreditWindow _requestCredits = new(ready.InitialReceiveWindowBytes);
    private int _disposed;

    /// <summary>
    /// Gets the identifier used to correlate lifecycle messages for the call.
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
    public IAsyncEnumerable<ExtensionRpcMessage> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _inbound.Reader.ReadAllAsync(cancellationToken);
    }

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
    public async ValueTask HandleInboundAsync(ExtensionRpcMessage message, CancellationToken cancellationToken)
    {
        if (message.ContentCase is ExtensionRpcMessage.ContentOneofCase.WindowUpdate)
        {
            _requestCredits.Add(message.WindowUpdate.ByteCount);
            return;
        }

        await _inbound.Writer.WriteAsync(message, cancellationToken);
        if (message.ContentCase is ExtensionRpcMessage.ContentOneofCase.Complete)
        {
            _inbound.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Completes the inbound response queue because its physical stream has ended.
    /// </summary>
    public void Complete()
    {
        _inbound.Writer.TryComplete();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is 0)
        {
            stream.RemoveCall(CallId);
            _inbound.Writer.TryComplete();
        }

        return ValueTask.CompletedTask;
    }
}
