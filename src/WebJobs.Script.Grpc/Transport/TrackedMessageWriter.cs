// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Grpc.Transport;

/// <summary>
/// Tracks queued messages until physical write/discard, rather than queue admission or HTTP caller departure.
/// </summary>
/// <typeparam name="T">The message type, tracked by reference identity.</typeparam>
/// <remarks>The queue owner must serialize writes and discard abandoned payloads before completing this tracker.</remarks>
public sealed class TrackedMessageWriter<T> where T : class
{
    private readonly Lock _lock = new();
    private readonly Dictionary<T, PendingWrite> _pending = new(ReferenceEqualityComparer.Instance);
    private Exception? _terminalError;

    /// <summary>
    /// Enqueues a message and waits for the writer's outcome; cancellation can discard only a not-yet-started write.
    /// </summary>
    /// <param name="writer">The destination queue writer.</param>
    /// <param name="message">The message to enqueue and track.</param>
    /// <param name="cancellationToken">Cancels queue admission or discards the message before its physical write begins.</param>
    /// <param name="prepare">An optional send-time callback that returns whether the physical write should proceed.</param>
    /// <returns>True after local write completion, or false if the message is discarded before writing.</returns>
    public async Task<bool> WriteAsync(ChannelWriter<T> writer, T message, CancellationToken cancellationToken,
        Func<bool>? prepare = null)
    {
        PendingWrite pending = new(cancellationToken, prepare);
        lock (_lock)
        {
            if (_terminalError is { } error)
            {
                throw error;
            }

            _pending.Add(message, pending);
        }

        try
        {
            await writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
            return await pending.Completion.Task.ConfigureAwait(false);
        }
        finally
        {
            lock (_lock)
            {
                _pending.Remove(message);
            }
        }
    }

    /// <summary>
    /// Lets the sole queue reader discard expired messages or update send-time state before starting a physical write.
    /// </summary>
    /// <param name="message">The dequeued message.</param>
    /// <returns><see langword="true"/> to write the message, or <see langword="false"/> to discard it.</returns>
    public bool Prepare(T message)
    {
        PendingWrite? pending;
        lock (_lock)
        {
            _pending.TryGetValue(message, out pending);
        }

        if (pending is null)
        {
            return true;
        }

        if (pending.Token.IsCancellationRequested)
        {
            pending.Completion.TrySetResult(false);
            return false;
        }

        if (pending.Prepare is { } prepare && !prepare())
        {
            pending.Completion.TrySetResult(false);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Signals successful local write completion, not peer processing or an HTTP terminal acknowledgment.
    /// </summary>
    /// <param name="message">The message whose physical write completed.</param>
    public void Written(T message)
    {
        lock (_lock)
        {
            if (_pending.TryGetValue(message, out PendingWrite? pending))
            {
                pending.Completion.TrySetResult(true);
            }
        }
    }

    /// <summary>
    /// Faults outstanding sends after the queue owner stops admission and discards all abandoned queued payloads.
    /// </summary>
    /// <param name="error">The terminal failure reported to outstanding and future sends.</param>
    public void Complete(Exception error)
    {
        lock (_lock)
        {
            _terminalError ??= error;
            foreach (PendingWrite pending in _pending.Values)
            {
                pending.Completion.TrySetException(_terminalError);
            }
        }
    }

    private sealed class PendingWrite(CancellationToken token, Func<bool>? prepare)
    {
        public CancellationToken Token { get; } = token;

        public Func<bool>? Prepare { get; } = prepare;

        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
