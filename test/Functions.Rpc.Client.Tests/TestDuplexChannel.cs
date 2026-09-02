// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc;

namespace Azure.Functions.Rpc.Client.Tests;

/// <summary>
/// Provides a replaceable duplex channel without creating network or gRPC resources.
/// </summary>
internal sealed class TestDuplexChannel<T> : DuplexChannel<T>
    where T : class
{
    private readonly TaskCompletionSource _allowDispose = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _disposeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Channel<T> _requests;
    private readonly Channel<T> _responses;
    private int _disposeCount;

    internal TestDuplexChannel(bool blockDisposal = false)
    {
        _requests = Channel.CreateUnbounded<T>();
        _responses = Channel.CreateUnbounded<T>();
        Reader = _responses.Reader;
        Writer = _requests.Writer;

        if (!blockDisposal)
        {
            _allowDispose.TrySetResult();
        }
    }

    /// <summary>
    /// Gets the number of times this channel performed its disposal transition.
    /// </summary>
    internal int DisposeCount => Interlocked.CompareExchange(ref _disposeCount, 0, 0);

    /// <summary>
    /// Gets a task that completes when asynchronous disposal starts.
    /// </summary>
    internal Task DisposeStarted => _disposeStarted.Task;

    /// <summary>
    /// Gets requests written by the channel consumer.
    /// </summary>
    internal ChannelReader<T> Requests => _requests.Reader;

    /// <summary>
    /// Allows a blocked asynchronous disposal operation to complete.
    /// </summary>
    internal void AllowDispose() => _allowDispose.TrySetResult();

    /// <summary>
    /// Completes both channel boundaries, optionally with a channel failure.
    /// </summary>
    /// <param name="exception">The channel failure, or <see langword="null"/> for clean completion.</param>
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

    protected override async ValueTask DisposeAsyncCore()
    {
        Interlocked.Increment(ref _disposeCount);
        _requests.Writer.TryComplete();
        _responses.Writer.TryComplete();
        _disposeStarted.TrySetResult();
        await _allowDispose.Task;
    }
}
