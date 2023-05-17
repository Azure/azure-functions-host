// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal class ConsoleWriter : IDisposable
    {
        // A typical out-of-proc function execution will generate 8 log lines.
        // A single core container can potentially get around 1K RPS at the higher end, and a typical log line is around 300 bytes
        // So in the extreme case, this is about 1 second of buffer and should be less than 3MB
        private const int DefaultBufferSize = 8000;

        // Because we read the log lines in batches from the buffer and write them to the console in one go,
        // we can influence the latency distribution by controlling how much of the buffer we will process in one pass.
        // If we set this to 1, the P50 latency will be low, but the P99 latency will be high.
        // If we set this to a large value, it keeps the P99 latency under control but the P50 degrades.
        // In local testing with a console attached, processing 1/10th of the buffer size per iteration yields single digit P50 while keeping P99 under 100ms.
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

        public ConsoleWriter(IEnvironment environment, Action<Exception> unhandledExceptionHandler)
            : this(environment, unhandledExceptionHandler, consoleBufferTimeout: DefaultConsoleBufferTimeout, autoStart: true)
        {
        }

        internal ConsoleWriter(IEnvironment environment, Action<Exception> exceptionHandler, TimeSpan consoleBufferTimeout, bool autoStart)
        {
            bool consoleEnabled = environment.GetEnvironmentVariable(EnvironmentSettingNames.ConsoleLoggingDisabled) != "1";

            if (consoleEnabled)
            {
                int maxBufferSize = environment.GetEnvironmentVariable(EnvironmentSettingNames.ConsoleLoggingBufferSize) switch
                {
                    var s when int.TryParse(s, out int i) && i >= 0 => i,
                    var s when int.TryParse(s, out int i) && i < 0 => throw new ArgumentOutOfRangeException(nameof(EnvironmentSettingNames.ConsoleLoggingBufferSize), "Console buffer size cannot be negative"),
                    _ => DefaultBufferSize,
                };

                if (maxBufferSize == 0)
                {
                    // buffer size was set to zero - disable it
                    _writeEvent = Console.WriteLine;
                }
                else
                {
                    _consoleBuffer = Channel.CreateBounded<string>(new BoundedChannelOptions(maxBufferSize) { SingleReader = true, SingleWriter = false });
                    _writeEvent = WriteToConsoleBuffer;
                    _consoleBufferTimeout = consoleBufferTimeout;
                    _writeResetEvent = new ManualResetEvent(true);
                    _maxLinesPerWrite = maxBufferSize / SingleWriteBufferDenominator;

                    if (autoStart)
                    {
                        StartProcessingBuffer();
                    }
                }
            }
            else
            {
                _writeEvent = (string s) => { };
            }

            _exceptionhandler = exceptionHandler;
        }

        public void WriteHandler(string evt)
        {
            _writeEvent(evt);
        }

        private void WriteToConsoleBuffer(string evt)
        {
            if (_consoleBuffer.Writer.TryWrite(evt) == false)
            {
                _writeResetEvent.Reset();
                if (_writeResetEvent.WaitOne(_consoleBufferTimeout) == false || _consoleBuffer.Writer.TryWrite(evt) == false)
                {
                    // We have either timed out or the buffer was full again, so just write directly to console
                    Console.WriteLine(evt);
                }
            }
        }

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

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_consoleBuffer != null)
                    {
                        _consoleBuffer.Writer.TryComplete();
                        _consoleBufferReadLoop.Wait(DisposeTimeout);
                    }

                    _writeResetEvent?.Dispose();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
