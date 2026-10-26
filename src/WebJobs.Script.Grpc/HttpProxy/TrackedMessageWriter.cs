// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Grpc.HttpProxy;

// Ownership ends at the physical write/discard, not when a message is queued or its HTTP caller leaves.
internal sealed class TrackedMessageWriter<T> where T : class
{
    private readonly Lock _lock = new();
    private readonly Dictionary<T, PendingWrite> _pending = new(ReferenceEqualityComparer.Instance);
    private Exception? _terminalError;

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
