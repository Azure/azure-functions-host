// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Azure.Functions.Rpc.Client.Tests;

/// <summary>
/// Provides a fully replaceable duplex channel for connection lifecycle, concurrency, and failure tests.
/// </summary>
internal sealed class TestDuplexChannel<T> : Channel<T>, IAsyncDisposable
    where T : class
{
    private readonly bool _blockWrites;
    private readonly CancellationTokenSource _disposeSource = new();
    private readonly CancellationToken _disposeToken;
    private readonly Exception _disposeException;
    private readonly TestChannelReader _reader;
    private readonly Channel<T> _responses = Channel.CreateUnbounded<T>();
    private readonly SemaphoreSlim _writeGate = new(0);
    private readonly Channel<T> _writeAttempts = Channel.CreateUnbounded<T>();
    private readonly TestChannelWriter _writer;
    private readonly Channel<T> _writes = Channel.CreateUnbounded<T>();
    private int _activeWrites;
    private int _disposeCount;
    private int _disposed;
    private int _maxConcurrentWrites;
    private int _readAttemptCount;
    private WriterCompletion _writerCompletion;

    internal TestDuplexChannel(bool blockWrites = false, Exception disposeException = null)
    {
        _blockWrites = blockWrites;
        _disposeException = disposeException;
        _disposeToken = _disposeSource.Token;
        _reader = new TestChannelReader(this);
        _writer = new TestChannelWriter(this);
        Reader = _reader;
        Writer = _writer;
    }

    internal int DisposeCount => Interlocked.CompareExchange(ref _disposeCount, 0, 0);

    internal int ActiveWriteCount => Interlocked.CompareExchange(ref _activeWrites, 0, 0);

    internal int MaxConcurrentWrites => Interlocked.CompareExchange(ref _maxConcurrentWrites, 0, 0);

    internal int ReadAttemptCount => Interlocked.CompareExchange(ref _readAttemptCount, 0, 0);

    internal ConcurrentQueue<T> WrittenMessages { get; } = new();

    internal ChannelReader<T> WriteAttempts => _writeAttempts.Reader;

    internal ChannelReader<T> Writes => _writes.Reader;

    internal void CompleteResponses(Exception exception = null)
    {
        CompleteRequests(exception);
        _responses.Writer.TryComplete(exception);
    }

    internal void CompleteRequests(Exception exception = null)
    {
        TryCompleteWriter(exception);
        if (_blockWrites)
        {
            _writeGate.Release();
        }
    }

    internal void FailWrites(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        CompleteResponses(exception);
    }

    internal ValueTask SendResponseAsync(T response, CancellationToken cancellationToken = default)
        => _responses.Writer.WriteAsync(response, cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Interlocked.Increment(ref _disposeCount);
            TryCompleteWriter(null);
            _disposeSource.Cancel();
            _responses.Writer.TryComplete();
            _writeAttempts.Writer.TryComplete();
            _writes.Writer.TryComplete();
            _disposeSource.Dispose();
        }

        return _disposeException is null
            ? ValueTask.CompletedTask
            : ValueTask.FromException(_disposeException);
    }

    private async ValueTask WriteAsync(T message, CancellationToken cancellationToken)
    {
        WriterCompletion writerCompletion = GetWriterCompletion();
        if (writerCompletion is not null)
        {
            throw new ChannelClosedException(writerCompletion.Exception);
        }

        using CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeToken);
        int activeWrites = Interlocked.Increment(ref _activeWrites);
        UpdateMaximum(ref _maxConcurrentWrites, activeWrites);

        try
        {
            await _writeAttempts.Writer.WriteAsync(message, linkedSource.Token);
            if (_blockWrites)
            {
                await _writeGate.WaitAsync(linkedSource.Token);
            }

            writerCompletion = GetWriterCompletion();
            if (writerCompletion is not null)
            {
                throw new ChannelClosedException(writerCompletion.Exception);
            }

            WrittenMessages.Enqueue(message);
            _writes.Writer.TryWrite(message);
        }
        finally
        {
            Interlocked.Decrement(ref _activeWrites);
        }
    }

    private bool TryCompleteWriter(Exception error)
    {
        WriterCompletion completion = new(error);
        if (Interlocked.CompareExchange(ref _writerCompletion, completion, null) is not null)
        {
            return false;
        }

        _writeAttempts.Writer.TryComplete(error);
        return true;
    }

    private WriterCompletion GetWriterCompletion() => Interlocked.CompareExchange(ref _writerCompletion, null, null);

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

    private sealed class TestChannelReader : ChannelReader<T>
    {
        private readonly TestDuplexChannel<T> _owner;

        internal TestChannelReader(TestDuplexChannel<T> owner)
        {
            _owner = owner;
        }

        public override Task Completion => _owner._responses.Reader.Completion;

        public override bool TryRead(out T item) => _owner._responses.Reader.TryRead(out item);

        public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _owner._readAttemptCount);
            return _owner._responses.Reader.WaitToReadAsync(cancellationToken);
        }
    }

    private sealed class TestChannelWriter : ChannelWriter<T>
    {
        private readonly TestDuplexChannel<T> _owner;

        internal TestChannelWriter(TestDuplexChannel<T> owner)
        {
            _owner = owner;
        }

        public override bool TryComplete(Exception error = null) => _owner.TryCompleteWriter(error);

        public override bool TryWrite(T item) => false;

        public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_owner.GetWriterCompletion() is null);
        }

        public override ValueTask WriteAsync(T item, CancellationToken cancellationToken = default)
            => _owner.WriteAsync(item, cancellationToken);
    }

    private sealed record WriterCompletion(Exception Exception);
}
