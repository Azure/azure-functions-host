// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Azure.Functions.Rpc.Client.Tests;

/// <summary>
/// Provides a fully replaceable duplex call for connection lifecycle, concurrency, and failure tests.
/// </summary>
internal sealed class TestDuplexCall<TRequest, TResponse> : IDuplexCall<TRequest, TResponse>
    where TRequest : class
    where TResponse : class
{
    private readonly bool _blockWrites;
    private readonly CancellationTokenSource _disposeSource = new();
    private readonly Exception _disposeException;
    private readonly Channel<TResponse> _responses = Channel.CreateUnbounded<TResponse>();
    private readonly Channel<TRequest> _writeAttempts = Channel.CreateUnbounded<TRequest>();
    private readonly SemaphoreSlim _writeGate = new(0);
    private Exception _writeException;
    private int _activeWrites;
    private int _disposeCount;
    private int _maxConcurrentWrites;
    private int _readAttemptCount;

    internal TestDuplexCall(bool blockWrites = false, Exception disposeException = null)
    {
        _blockWrites = blockWrites;
        _disposeException = disposeException;
    }

    internal int DisposeCount => Interlocked.CompareExchange(ref _disposeCount, 0, 0);

    internal int MaxConcurrentWrites => Interlocked.CompareExchange(ref _maxConcurrentWrites, 0, 0);

    internal int ReadAttemptCount => Interlocked.CompareExchange(ref _readAttemptCount, 0, 0);

    internal ConcurrentQueue<TRequest> WrittenMessages { get; } = new();

    internal ChannelReader<TRequest> WriteAttempts => _writeAttempts.Reader;

    /// <inheritdoc />
    public async Task WriteAsync(TRequest request)
    {
        int activeWrites = Interlocked.Increment(ref _activeWrites);
        UpdateMaximum(ref _maxConcurrentWrites, activeWrites);

        try
        {
            await _writeAttempts.Writer.WriteAsync(request, _disposeSource.Token);
            if (_blockWrites)
            {
                await _writeGate.WaitAsync(_disposeSource.Token);
            }

            if (_writeException is not null)
            {
                ExceptionDispatchInfo.Capture(_writeException).Throw();
            }

            WrittenMessages.Enqueue(request);
        }
        finally
        {
            Interlocked.Decrement(ref _activeWrites);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TResponse> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeSource.Token);
        while (true)
        {
            TResponse response;
            try
            {
                Interlocked.Increment(ref _readAttemptCount);
                response = await _responses.Reader.ReadAsync(linkedSource.Token);
            }
            catch (ChannelClosedException exception) when (exception.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
            catch (ChannelClosedException)
            {
                yield break;
            }

            yield return response;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Increment(ref _disposeCount) == 1)
        {
            _disposeSource.Cancel();
            _responses.Writer.TryComplete();
            _writeAttempts.Writer.TryComplete();
            _disposeSource.Dispose();
        }

        return _disposeException is null
            ? ValueTask.CompletedTask
            : ValueTask.FromException(_disposeException);
    }

    internal void CompleteResponses(Exception exception = null) => _responses.Writer.TryComplete(exception);

    internal void FailWrites(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _writeException = exception;
        _writeGate.Release();
    }

    internal void ReleaseWrite() => _writeGate.Release();

    internal ValueTask SendResponseAsync(TResponse response, CancellationToken cancellationToken = default)
        => _responses.Writer.WriteAsync(response, cancellationToken);

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        int current;
        do
        {
            current = Interlocked.CompareExchange(ref maximum, 0, 0);
            if (candidate <= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref maximum, candidate, current) != current);
    }
}
