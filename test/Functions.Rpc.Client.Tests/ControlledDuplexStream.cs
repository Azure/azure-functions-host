// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Grpc.Core;

namespace Azure.Functions.Rpc.Client.Tests;

internal sealed class ControlledDuplexStream<T>
{
    private readonly CancellationToken _connectionCancellationToken;
    private readonly bool _blockWrites;
    private readonly Channel<T> _responses = Channel.CreateUnbounded<T>();
    private readonly Channel<T> _writeAttempts = Channel.CreateUnbounded<T>();
    private readonly SemaphoreSlim _writeGate = new(0);
    private Exception _writeException;
    private int _activeWrites;
    private int _disposeCount;
    private int _maxConcurrentWrites;
    private int _moveNextCount;

    internal ControlledDuplexStream(CancellationToken connectionCancellationToken, bool blockWrites = false)
    {
        _connectionCancellationToken = connectionCancellationToken;
        _blockWrites = blockWrites;
        RequestStream = new ControlledClientStreamWriter(this);
        ResponseStream = new ControlledAsyncStreamReader(this);
        Call = new AsyncDuplexStreamingCall<T, T>(
            RequestStream,
            ResponseStream,
            Task.FromResult(new Metadata()),
            static () => Status.DefaultSuccess,
            static () => new Metadata(),
            () => Interlocked.Increment(ref _disposeCount));
    }

    internal AsyncDuplexStreamingCall<T, T> Call { get; }

    internal int DisposeCount => Interlocked.CompareExchange(ref _disposeCount, 0, 0);

    internal int MaxConcurrentWrites => Interlocked.CompareExchange(ref _maxConcurrentWrites, 0, 0);

    internal int MoveNextCount => Interlocked.CompareExchange(ref _moveNextCount, 0, 0);

    internal ControlledClientStreamWriter RequestStream { get; }

    internal ConcurrentQueue<T> WrittenMessages { get; } = new();

    internal ChannelReader<T> WriteAttempts => _writeAttempts.Reader;

    internal ControlledAsyncStreamReader ResponseStream { get; }

    internal ValueTask SendResponseAsync(T value, CancellationToken cancellationToken = default)
        => _responses.Writer.WriteAsync(value, cancellationToken);

    internal void CompleteResponses(Exception exception = null) => _responses.Writer.TryComplete(exception);

    internal void FailWrites(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _writeException = exception;
        _writeGate.Release();
    }

    internal void ReleaseWrite() => _writeGate.Release();

    internal sealed class ControlledAsyncStreamReader : IAsyncStreamReader<T>
    {
        private readonly ControlledDuplexStream<T> _owner;

        internal ControlledAsyncStreamReader(ControlledDuplexStream<T> owner)
        {
            _owner = owner;
        }

        public T Current { get; private set; }

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _owner._moveNextCount);
            using CancellationTokenSource linkedSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _owner._connectionCancellationToken);

            try
            {
                Current = await _owner._responses.Reader.ReadAsync(linkedSource.Token);
                return true;
            }
            catch (ChannelClosedException exception) when (exception.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
            catch (ChannelClosedException)
            {
                return false;
            }
        }
    }

    internal sealed class ControlledClientStreamWriter : IClientStreamWriter<T>
    {
        private readonly ControlledDuplexStream<T> _owner;

        internal ControlledClientStreamWriter(ControlledDuplexStream<T> owner)
        {
            _owner = owner;
        }

        public WriteOptions WriteOptions { get; set; }

        public Task CompleteAsync() => Task.CompletedTask;

        public async Task WriteAsync(T message)
        {
            int activeWrites = Interlocked.Increment(ref _owner._activeWrites);
            UpdateMaximum(ref _owner._maxConcurrentWrites, activeWrites);

            try
            {
                await _owner._writeAttempts.Writer.WriteAsync(message, _owner._connectionCancellationToken);
                if (_owner._blockWrites)
                {
                    await _owner._writeGate.WaitAsync(_owner._connectionCancellationToken);
                }

                if (_owner._writeException is not null)
                {
                    throw _owner._writeException;
                }

                _owner.WrittenMessages.Enqueue(message);
            }
            finally
            {
                Interlocked.Decrement(ref _owner._activeWrites);
            }
        }

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
}
