// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Grpc.HttpProxy;

/// <summary>
/// Tracks queued messages until physical write/discard, rather than queue admission or HTTP caller departure.
/// </summary>
/// <remarks>The queue owner must serialize writes and discard abandoned payloads before completing this tracker.</remarks>
internal sealed class TrackedMessageWriter<T> where T : class
{
    private readonly Lock _lock = new();
    private readonly Dictionary<T, PendingWrite> _pending = new(ReferenceEqualityComparer.Instance);
    private Exception? _terminalError;

    /// <summary>
    /// Enqueues a message and waits for the writer's outcome; cancellation can discard only a not-yet-started write.
    /// </summary>
    /// <returns>True after local write completion, or false if the message is discarded before writing.</returns>
    internal async Task<bool> WriteAsync(ChannelWriter<T> writer, T message, CancellationToken cancellationToken,
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
    internal bool Prepare(T message)
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
    internal void Written(T message)
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
    internal void Complete(Exception error)
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
