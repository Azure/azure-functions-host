// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Azure.Functions.Rpc.Client.Tests;

/// <summary>
/// Provides a replaceable duplex channel without creating network or gRPC resources.
/// </summary>
internal sealed class TestDuplexChannel<T> : Channel<T>, IAsyncDisposable
    where T : class
{
    private readonly Channel<T> _requests;
    private readonly Channel<T> _responses;
    private int _disposeCount;
    private int _disposed;

    internal TestDuplexChannel()
    {
        _requests = Channel.CreateUnbounded<T>();
        _responses = Channel.CreateUnbounded<T>();
        Reader = _responses.Reader;
        Writer = _requests.Writer;
    }

    /// <summary>
    /// Gets the number of times this channel performed its disposal transition.
    /// </summary>
    internal int DisposeCount => Interlocked.CompareExchange(ref _disposeCount, 0, 0);

    /// <summary>
    /// Gets requests written by the channel consumer.
    /// </summary>
    internal ChannelReader<T> Requests => _requests.Reader;

    /// <summary>
    /// Completes the response boundary, optionally with a transport failure.
    /// </summary>
    /// <param name="exception">The transport failure, or <see langword="null"/> for clean completion.</param>
    internal void CompleteResponses(Exception exception = null)
    {
        _requests.Writer.TryComplete(exception);
        _responses.Writer.TryComplete(exception);
    }

    /// <summary>
    /// Supplies one response for the channel consumer to read.
    /// </summary>
    /// <param name="response">The response to supply.</param>
    /// <param name="cancellationToken">A token that cancels this write.</param>
    /// <returns>A task that completes when the response is queued.</returns>
    internal ValueTask SendResponseAsync(T response, CancellationToken cancellationToken = default)
        => _responses.Writer.WriteAsync(response, cancellationToken);

    /// <summary>
    /// Completes both channel boundaries.
    /// </summary>
    /// <returns>A completed task.</returns>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Interlocked.Increment(ref _disposeCount);
            _requests.Writer.TryComplete();
            _responses.Writer.TryComplete();
        }

        return ValueTask.CompletedTask;
    }
}
