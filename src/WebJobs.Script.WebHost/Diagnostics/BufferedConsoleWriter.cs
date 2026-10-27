// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal enum BufferedConsoleWriterFullMode
    {
        WaitAndWriteDirectly,
        DropWrite
    }

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
        private readonly Channel<string> _consoleBuffer;
        private readonly TimeSpan _consoleBufferTimeout;
        private readonly Action<Exception> _exceptionhandler;
        private readonly Func<long, string>? _droppedWriteSummaryFactory;
        private readonly BufferedConsoleWriterFullMode _fullMode;
        private readonly int _maxLinesPerWrite;
        private Task? _consoleBufferReadLoop;
        private Func<string, bool> _writeEvent;
        private bool _disposed;
        private long _pendingDroppedWrites;
        private long _totalDroppedWrites;

        public BufferedConsoleWriter(int bufferSize, Action<Exception> exceptionHandler)
            : this(
                  bufferSize,
                  exceptionHandler,
                  consoleBufferTimeout: DefaultConsoleBufferTimeout,
                  autoStart: true,
                  BufferedConsoleWriterFullMode.WaitAndWriteDirectly)
        {
        }

        internal BufferedConsoleWriter(
            int bufferSize,
            Action<Exception> exceptionHandler,
            TimeSpan consoleBufferTimeout,
            bool autoStart,
            BufferedConsoleWriterFullMode fullMode = BufferedConsoleWriterFullMode.WaitAndWriteDirectly,
            Func<long, string>? droppedWriteSummaryFactory = null)
        {
            _consoleBuffer = Channel.CreateBounded<string>(new BoundedChannelOptions(bufferSize) { SingleReader = true, SingleWriter = false });
            _writeEvent = WriteToConsoleBuffer;
            _consoleBufferTimeout = consoleBufferTimeout;
            _writeResetEvent = new ManualResetEvent(true);
            _maxLinesPerWrite = bufferSize / SingleWriteBufferDenominator;
            _fullMode = fullMode;
            _droppedWriteSummaryFactory = droppedWriteSummaryFactory;
            _exceptionhandler = exceptionHandler ?? throw new ArgumentNullException(nameof(exceptionHandler));

            if (autoStart)
            {
                StartProcessingBuffer();
            }
        }

        public TextWriter Writer { get; init; } = Console.Out;

        internal long TotalDroppedWrites => Interlocked.Read(ref _totalDroppedWrites);

        public void WriteHandler(string evt)
        {
            _ = TryWrite(evt);
        }

        internal bool TryWrite(string evt)
        {
            return _writeEvent(evt);
        }

        private bool WriteToConsoleBuffer(string evt)
        {
            if (_consoleBuffer.Writer.TryWrite(evt))
            {
                return true;
            }

            if (_fullMode is BufferedConsoleWriterFullMode.DropWrite)
            {
                RecordDrop();
                return false;
            }

            _writeResetEvent.Reset();
            bool waitFailed = !_writeResetEvent.WaitOne(_consoleBufferTimeout);

            // If the wait failed, write to the console. Otherwise, try the writing again - if that fails, write to the console.
            if (waitFailed || !_consoleBuffer.Writer.TryWrite(evt))
            {
                Writer.WriteLine(evt);
            }

            return true;
        }

        // internal only for testing - should only be used from the constructor, not intended for concurrent callers.
        internal void StartProcessingBuffer()
        {
            // intentional no-op if the task is already running
            if (_consoleBufferReadLoop == null || _consoleBufferReadLoop.IsCompleted)
            {
                _consoleBufferReadLoop = ProcessConsoleBufferAsync();
            }
        }

        private async Task ProcessConsoleBufferAsync()
        {
            int inFlightLines = 0;
            try
            {
                var builder = new StringBuilder();

                while (await _consoleBuffer.Reader.WaitToReadAsync())
                {
                    if (_consoleBuffer.Reader.TryRead(out string? line1) && line1 is not null)
                    {
                        inFlightLines = 1;
                        _writeResetEvent.Set();
                        await WriteDroppedSummaryAsync();

                        // Can we synchronously read multiple lines?
                        // If yes, use the string builder to batch them together into a single write
                        // If no, just write the single line without using the builder;
                        if (_consoleBuffer.Reader.TryRead(out string? line2) && line2 is not null)
                        {
                            builder.AppendLine(line1);
                            builder.AppendLine(line2);
                            int lines = 2;
                            inFlightLines = lines;

                            while (lines < _maxLinesPerWrite
                                && _consoleBuffer.Reader.TryRead(out string? nextLine)
                                && nextLine is not null)
                            {
                                builder.AppendLine(nextLine);
                                lines++;
                                inFlightLines = lines;
                            }

                            _writeResetEvent.Set();
                            await Writer.WriteAsync(builder.ToString());
                            builder.Clear();
                            inFlightLines = 0;
                        }
                        else
                        {
                            await Writer.WriteLineAsync(line1);
                            inFlightLines = 0;
                        }
                    }
                }

                await WriteDroppedSummaryAsync();
            }
            catch (Exception ex)
            {
                if (_fullMode is BufferedConsoleWriterFullMode.DropWrite && inFlightLines > 0)
                {
                    RecordDrops(inFlightLines);
                }

                // Not sure what can fail here, but there is nothing else monitoring this task so just log the exception
                _exceptionhandler(ex);
            }
            finally
            {
                if (_fullMode is BufferedConsoleWriterFullMode.WaitAndWriteDirectly)
                {
                    // if this has failed for any reason, fall everything back to console
                    _writeEvent = WriteDirectly;
                    _consoleBuffer.Writer.TryComplete();
                }
                else
                {
                    _consoleBuffer.Writer.TryComplete();
                    _writeEvent = DropWrite;
                    while (_consoleBuffer.Reader.TryRead(out _))
                    {
                        RecordDrop();
                    }
                }
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_consoleBuffer != null)
                {
                    _consoleBuffer.Writer.TryComplete();
                    _consoleBufferReadLoop?.Wait(DisposeTimeout);
                }

                _writeResetEvent?.Dispose();
                _disposed = true;
                _writeEvent = _fullMode is BufferedConsoleWriterFullMode.WaitAndWriteDirectly
                    ? WriteDirectly
                    : DropWrite;
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
            return _consoleBufferReadLoop ?? Task.CompletedTask;
        }

        private bool WriteDirectly(string evt)
        {
            Writer.WriteLine(evt);
            return true;
        }

        private bool DropWrite(string evt)
        {
            RecordDrop();
            return false;
        }

        private async Task WriteDroppedSummaryAsync()
        {
            if (_droppedWriteSummaryFactory is null)
            {
                return;
            }

            long droppedWrites = Interlocked.Exchange(ref _pendingDroppedWrites, 0);
            if (droppedWrites > 0)
            {
                await Writer.WriteLineAsync(_droppedWriteSummaryFactory(droppedWrites));
            }
        }

        private void RecordDrop()
        {
            RecordDrops(1);
        }

        private void RecordDrops(int count)
        {
            Interlocked.Add(ref _pendingDroppedWrites, count);
            Interlocked.Add(ref _totalDroppedWrites, count);
        }
    }
}
