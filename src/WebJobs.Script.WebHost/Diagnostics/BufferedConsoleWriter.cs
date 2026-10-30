// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

#nullable enable

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal sealed class BufferedConsoleWriter : IDisposable
    {
        // Because we read the log lines in batches from the buffer and write them to the console in one go,
        // we can influence the latency distribution by controlling how much of the buffer we will process in one pass.
        // If we set this to 1, the P50 latency will be low, but the P99 latency will be high.
        // If we set this to a large value, it keeps the P99 latency under control but the P50 degrades.
        // In local testing with a console attached, processing 1/10th of the buffer size per iteration yields P50 under 10ms with P99 under 100ms.
        private const int SingleWriteBufferDenominator = 10;

        private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan DefaultConsoleBufferTimeout = TimeSpan.FromSeconds(1);
        private readonly ManualResetEvent _writeResetEvent;
        private readonly Channel<ConsoleBufferItem> _consoleBuffer;
        private readonly TimeSpan _consoleBufferTimeout;
        private readonly Action<Exception> _exceptionhandler;
        private readonly int _maxLinesPerWrite;
        private Task _consoleBufferReadLoop = Task.CompletedTask;
        private Exception? _bufferFailure;
        private Action<string> _writeEvent;
        private bool _disposed;

        public BufferedConsoleWriter(int bufferSize, Action<Exception> exceptionHandler)
            : this(bufferSize, exceptionHandler, consoleBufferTimeout: DefaultConsoleBufferTimeout, autoStart: true)
        {
        }

        internal BufferedConsoleWriter(int bufferSize, Action<Exception> exceptionHandler, TimeSpan consoleBufferTimeout, bool autoStart)
        {
            _consoleBuffer = Channel.CreateBounded<ConsoleBufferItem>(new BoundedChannelOptions(bufferSize) { SingleReader = true, SingleWriter = false });
            _writeEvent = WriteToConsoleBuffer;
            _consoleBufferTimeout = consoleBufferTimeout;
            _writeResetEvent = new ManualResetEvent(true);
            _maxLinesPerWrite = Math.Max(1, bufferSize / SingleWriteBufferDenominator);
            _exceptionhandler = exceptionHandler;

            if (autoStart)
            {
                StartProcessingBuffer();
            }
        }

        public TextWriter Writer { get; init; } = Console.Out;

        public void WriteHandler(string evt)
        {
            _writeEvent(evt);
        }

        private void WriteToConsoleBuffer(string evt)
        {
            ConsoleBufferItem item = new(evt, null);
            if (!_consoleBuffer.Writer.TryWrite(item))
            {
                _writeResetEvent.Reset();
                bool waitFailed = !_writeResetEvent.WaitOne(_consoleBufferTimeout);

                // If the wait failed, write to the console. Otherwise, try the writing again - if that fails, write to the console.
                if (waitFailed || !_consoleBuffer.Writer.TryWrite(item))
                {
                    Writer.WriteLine(evt);
                }
            }
        }

        // internal only for testing - should only be used from the constructor, not intended for concurrent callers.
        internal void StartProcessingBuffer()
        {
            // intentional no-op if the task is already running
            if (_consoleBufferReadLoop.IsCompleted)
            {
                _consoleBufferReadLoop = ProcessConsoleBufferAsync();
            }
        }

        private async Task ProcessConsoleBufferAsync()
        {
            TaskCompletionSource? flush = null;
            try
            {
                var builder = new StringBuilder();

                while (await _consoleBuffer.Reader.WaitToReadAsync())
                {
                    string? firstLine = null;
                    int lines = 0;
                    while (lines < _maxLinesPerWrite && _consoleBuffer.Reader.TryRead(out ConsoleBufferItem item))
                    {
                        if (lines == 0)
                        {
                            _writeResetEvent.Set();
                        }
                        if (item.Completion is not null)
                        {
                            flush = item.Completion;
                            break;
                        }

                        if (++lines == 1)
                        {
                            firstLine = item.Line;
                        }
                        else
                        {
                            if (lines == 2)
                            {
                                builder.AppendLine(firstLine);
                            }

                            builder.AppendLine(item.Line);
                        }
                    }

                    if (lines > 1 || flush is not null)
                    {
                        _writeResetEvent.Set();
                    }

                    if (lines == 1)
                    {
                        await Writer.WriteLineAsync(firstLine);
                    }
                    else if (lines > 1)
                    {
                        await Writer.WriteAsync(builder.ToString());
                        builder.Clear();
                    }

                    if (flush is not null)
                    {
                        await Writer.FlushAsync();
                        flush.TrySetResult();
                        flush = null;
                    }
                }
            }
            catch (Exception ex)
            {
                _bufferFailure = ex;
                flush?.TrySetException(ex);
                // Not sure what can fail here, but there is nothing else monitoring this task so just log the exception
                _exceptionhandler(ex);
            }
            finally
            {
                // if this has failed for any reason, fall everything back to console
                _writeEvent = Writer.WriteLine;
                _consoleBuffer.Writer.TryComplete();
                while (_consoleBuffer.Reader.TryRead(out ConsoleBufferItem item))
                {
                    item.Completion?.TrySetException(
                        _bufferFailure ?? new ObjectDisposedException(nameof(BufferedConsoleWriter)));
                }
            }
        }

        /// <summary>
        /// Waits for preceding buffered lines to be written and flushes the output without stopping the writer.
        /// </summary>
        /// <param name="cancellationToken">Limits the wait for the flush.</param>
        /// <returns>A task that completes when the output has been flushed.</returns>
        public async Task FlushAsync(CancellationToken cancellationToken = default)
        {
            TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                await _consoleBuffer.Writer.WriteAsync(new(string.Empty, completion), cancellationToken);
            }
            catch (ChannelClosedException)
            {
                await _consoleBufferReadLoop.WaitAsync(cancellationToken);
                if (_bufferFailure is not null)
                {
                    throw new InvalidOperationException("The console buffer failed before it could be flushed.", _bufferFailure);
                }

                await Writer.FlushAsync(cancellationToken);
                return;
            }

            await completion.Task.WaitAsync(cancellationToken);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _consoleBuffer.Writer.TryComplete();
                _consoleBufferReadLoop.Wait(DisposeTimeout);

                _writeResetEvent.Dispose();
                _disposed = true;
            }

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Primarily for testing. Do not call in production.
        /// </summary>
        /// <remarks>
        /// Not called 'FlushAsync' because this does stop processing messages.
        /// </remarks>
        /// <returns>A task that completes when all buffered messages are drained.</returns>
        internal Task CompleteAsync()
        {
            _consoleBuffer.Writer.TryComplete();
            return _consoleBufferReadLoop;
        }

        private readonly record struct ConsoleBufferItem(string Line, TaskCompletionSource? Completion);
    }
}
