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

public partial class GrpcDuplexStreamTests
{
    /// <summary>
    /// Builds a real <see cref="AsyncDuplexStreamingCall{TRequest, TResponse}"/> over mock request and response streams so
    /// <see cref="GrpcDuplexStream{T}"/> can be tested without a network connection.
    /// Tests can supply peer responses, block writes, and observe successful writes and disposal deterministically.
    /// </summary>
    /// <typeparam name="T">The value type used by both sides of the test call.</typeparam>
    private sealed class MockDuplexStream<T>
    {
        private readonly bool _blockWrites;
        private readonly CancellationTokenSource _callCancellationSource = new();
        private readonly Exception _disposeException;
        private readonly Channel<T> _responses = Channel.CreateUnbounded<T>();
        private readonly TaskCompletionSource _requestCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Channel<T> _writeAttempts = Channel.CreateUnbounded<T>();
        private readonly SemaphoreSlim _writeGate = new(0);
        private int _disposeCount;
        private Exception _writeException;

        /// <summary>
        /// Initializes a new instance of the <see cref="MockDuplexStream{T}"/> class.
        /// </summary>
        /// <param name="blockWrites">Whether request writes should wait until the mocked call is cancelled.</param>
        /// <param name="disposeException">An optional failure reported when the SDK call is disposed.</param>
        internal MockDuplexStream(bool blockWrites = false, Exception disposeException = null)
        {
            _blockWrites = blockWrites;
            _disposeException = disposeException;
            MockClientStreamWriter requestStream = new(this);
            MockAsyncStreamReader responseStream = new(this);
            Call = new AsyncDuplexStreamingCall<T, T>(requestStream, responseStream, Task.FromResult(new Metadata()),
                static () => Status.DefaultSuccess, static () => new Metadata(), DisposeCall);
        }

        /// <summary>
        /// Gets the SDK call assembled from the mock streams.
        /// </summary>
        internal AsyncDuplexStreamingCall<T, T> Call { get; }

        /// <summary>
        /// Gets the number of times the SDK call was disposed.
        /// </summary>
        internal int DisposeCount => Interlocked.CompareExchange(ref _disposeCount, 0, 0);

        /// <summary>
        /// Gets a task that completes when the adapter half-closes the SDK request stream.
        /// </summary>
        internal Task RequestCompleted => _requestCompleted.Task;

        /// <summary>
        /// Gets the requests successfully written to the mock stream.
        /// </summary>
        internal ConcurrentQueue<T> WrittenMessages { get; } = new();

        /// <summary>
        /// Gets a reader that signals each request-stream write attempt before any configured blocking or failure.
        /// </summary>
        internal ChannelReader<T> WriteAttempts => _writeAttempts.Reader;

        /// <summary>
        /// Supplies one response for the mock peer to return.
        /// </summary>
        /// <param name="value">The response value.</param>
        /// <returns>A task that completes when the response is queued.</returns>
        internal ValueTask SendResponseAsync(T value) => _responses.Writer.WriteAsync(value);

        /// <summary>
        /// Completes the response stream.
        /// </summary>
        internal void CompleteResponses(Exception exception = null) => _responses.Writer.TryComplete(exception);

        /// <summary>
        /// Fails a blocked request-stream write.
        /// </summary>
        /// <param name="exception">The failure to report.</param>
        internal void FailWrites(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            Interlocked.Exchange(ref _writeException, exception);
            _writeGate.Release();
        }

        private void DisposeCall()
        {
            Interlocked.Increment(ref _disposeCount);
            _callCancellationSource.Cancel();
            _callCancellationSource.Dispose();
            if (_disposeException is not null)
            {
                throw _disposeException;
            }
        }

        /// <summary>
        /// Implements the SDK response stream over the mock peer-response channel.
        /// </summary>
        private sealed class MockAsyncStreamReader : IAsyncStreamReader<T>
        {
            private readonly MockDuplexStream<T> _owner;

            /// <summary>
            /// Initializes a new instance of the <see cref="MockAsyncStreamReader"/> class.
            /// </summary>
            /// <param name="owner">The owning mock duplex stream.</param>
            internal MockAsyncStreamReader(MockDuplexStream<T> owner)
            {
                _owner = owner;
            }

            /// <inheritdoc />
            public T Current { get; private set; }

            /// <inheritdoc />
            public async Task<bool> MoveNext(CancellationToken cancellationToken)
            {
                using CancellationTokenSource linkedSource =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _owner._callCancellationSource.Token);

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

        /// <summary>
        /// Implements the SDK request stream with deterministic write observation, blocking, and failure injection.
        /// </summary>
        private sealed class MockClientStreamWriter : IClientStreamWriter<T>
        {
            private readonly MockDuplexStream<T> _owner;

            /// <summary>
            /// Initializes a new instance of the <see cref="MockClientStreamWriter"/> class.
            /// </summary>
            /// <param name="owner">The owning mock duplex stream.</param>
            internal MockClientStreamWriter(MockDuplexStream<T> owner)
            {
                _owner = owner;
            }

            /// <inheritdoc />
            public WriteOptions WriteOptions { get; set; }

            /// <inheritdoc />
            public Task CompleteAsync()
            {
                _owner._requestCompleted.TrySetResult();
                return Task.CompletedTask;
            }

            /// <inheritdoc />
            public async Task WriteAsync(T message)
            {
                await _owner._writeAttempts.Writer.WriteAsync(message, _owner._callCancellationSource.Token);
                if (_owner._blockWrites)
                {
                    await _owner._writeGate.WaitAsync(_owner._callCancellationSource.Token);
                }

                Exception writeException = Interlocked.CompareExchange(ref _owner._writeException, null, null);
                if (writeException is not null)
                {
                    ExceptionDispatchInfo.Capture(writeException).Throw();
                }

                _owner.WrittenMessages.Enqueue(message);
            }
        }
    }
}
