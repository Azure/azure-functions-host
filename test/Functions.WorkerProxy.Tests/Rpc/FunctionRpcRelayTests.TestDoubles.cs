// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    private sealed class GatedFaultingStreamReader : IAsyncStreamReader<StreamingMessage>
    {
        private readonly Task _releaseFault;
        private readonly Exception _exception;

        public GatedFaultingStreamReader(Task releaseFault, Exception exception)
        {
            _releaseFault = releaseFault;
            _exception = exception;
        }

        public StreamingMessage Current { get; } = new();

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            await _releaseFault.WaitAsync(cancellationToken);
            throw _exception;
        }
    }

    private sealed class BlockingStreamReader : IAsyncStreamReader<StreamingMessage>
    {
        public StreamingMessage Current { get; } = new();

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            return false;
        }
    }

    private sealed class SingleMessageThenBlockStreamReader : IAsyncStreamReader<StreamingMessage>
    {
        private int _messageRead;

        public SingleMessageThenBlockStreamReader(StreamingMessage message)
        {
            Current = message;
        }

        public StreamingMessage Current { get; }

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _messageRead, 1) == 0)
            {
                return true;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            return false;
        }
    }

    private sealed class TestServerStreamWriter : IServerStreamWriter<StreamingMessage>
    {
        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(StreamingMessage message)
        {
            return Task.CompletedTask;
        }

        public Task WriteAsync(StreamingMessage message, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingServerStreamWriter : IServerStreamWriter<StreamingMessage>
    {
        private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _writeEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WriteEntered => _writeEntered.Task;

        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(StreamingMessage message)
        {
            return WriteAsync(message, CancellationToken.None);
        }

        public async Task WriteAsync(StreamingMessage message, CancellationToken cancellationToken)
        {
            _writeEntered.TrySetResult(true);
            await _release.Task;
        }

        public void Release()
        {
            _release.TrySetResult(true);
        }
    }

    private sealed class BlockingLogger<T> : ILogger<T>, IDisposable
    {
        private readonly ManualResetEventSlim _release = new(initialState: false);
        private readonly TaskCompletionSource<bool> _logEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _logCount;

        public Task LogEntered => _logEntered.Task;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (Interlocked.Increment(ref _logCount) == 1)
            {
                _logEntered.TrySetResult(true);
                _release.Wait();
            }
        }

        public void Release()
        {
            _release.Set();
        }

        public void Dispose()
        {
            _release.Dispose();
        }
    }
}
