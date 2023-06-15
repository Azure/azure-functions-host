// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

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
        private readonly Channel<string> _consoleBuffer;
        private readonly TimeSpan _consoleBufferTimeout;
        private readonly Action<Exception> _exceptionhandler;
        private readonly int _maxLinesPerWrite;
        private Task _consoleBufferReadLoop;
        private Action<string> _writeEvent;
        private bool _disposed;

        public BufferedConsoleWriter(int bufferSize, Action<Exception> exceptionHandler)
            : this(bufferSize, exceptionHandler, consoleBufferTimeout: DefaultConsoleBufferTimeout, autoStart: true)
        {
        }

        internal BufferedConsoleWriter(int bufferSize, Action<Exception> exceptionHandler, TimeSpan consoleBufferTimeout, bool autoStart)
        {
            _consoleBuffer = Channel.CreateBounded<string>(new BoundedChannelOptions(bufferSize) { SingleReader = true, SingleWriter = false });
            _writeEvent = WriteToConsoleBuffer;
            _consoleBufferTimeout = consoleBufferTimeout;
            _writeResetEvent = new ManualResetEvent(true);
            _maxLinesPerWrite = bufferSize / SingleWriteBufferDenominator;

            if (autoStart)
            {
                StartProcessingBuffer();
            }

            _exceptionhandler = exceptionHandler;
        }

        public void WriteHandler(string evt)
        {
            _writeEvent(evt);
        }

        private void WriteToConsoleBuffer(string evt)
        {
            if (!_consoleBuffer.Writer.TryWrite(evt))
            {
                _writeResetEvent.Reset();
                bool waitFailed = !_writeResetEvent.WaitOne(_consoleBufferTimeout);

                // If the wait failed, write to the console. Otherwise, try the writing again - if that fails, write to the console.
                if (waitFailed || !_consoleBuffer.Writer.TryWrite(evt))
                {
                    Console.WriteLine(evt);
                }
            }
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
            try
            {
                var builder = new StringBuilder();

                while (await _consoleBuffer.Reader.WaitToReadAsync())
                {
                    if (_consoleBuffer.Reader.TryRead(out string line1))
                    {
                        _writeResetEvent.Set();

                        // Can we synchronously read multiple lines?
                        // If yes, use the string builder to batch them together into a single write
                        // If no, just write the single line without using the builder;
                        if (_consoleBuffer.Reader.TryRead(out string line2))
                        {
                            builder.AppendLine(line1);
                            builder.AppendLine(line2);
                            int lines = 2;

                            while (lines < _maxLinesPerWrite && _consoleBuffer.Reader.TryRead(out string nextLine))
                            {
                                builder.AppendLine(nextLine);
                                lines++;
                            }

                            _writeResetEvent.Set();
                            Console.Write(builder.ToString());
                            builder.Clear();
                        }
                        else
                        {
                            Console.WriteLine(line1);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Not sure what can fail here, but there is nothing else monitoring this task so just log the exception
                _exceptionhandler(ex);
            }
            finally
            {
                // if this has failed for any reason, fall everything back to console
                _writeEvent = Console.WriteLine;
                _consoleBuffer.Writer.TryComplete();
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_consoleBuffer != null)
                {
                    _consoleBuffer.Writer.TryComplete();
                    _consoleBufferReadLoop.Wait(DisposeTimeout);
                }

                _writeResetEvent?.Dispose();
                _disposed = true;
            }

            GC.SuppressFinalize(this);
        }
    }
}
