// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal class ConsoleWriter
    {
        // A typical out-of-proc function execution will generate 8 log lines.
        // A single core container can potentially get around 1K RPS at the higher end, and a typical log line is around 300 bytes
        // So in the extreme case, this is about 1 second of buffer and should be less than 3MB
        private const int DefaultBufferSize = 8000;

        private static readonly TimeSpan DefaultConsoleBufferTimeout = TimeSpan.FromSeconds(1);
        private readonly Channel<string> _consoleBuffer;
        private readonly TimeSpan _consoleBufferTimeout;
        private readonly Action<Exception> _exceptionhandler;
        private Task _consoleBufferReadLoop;
        private Action<string> _writeEvent;

        public ConsoleWriter(IEnvironment environment, Action<Exception> unhandledExceptionHandler)
            : this(environment, unhandledExceptionHandler, consoleBufferTimeout: DefaultConsoleBufferTimeout, autoStart: true)
        {
        }

        internal ConsoleWriter(IEnvironment environment, Action<Exception> exceptionHandler, TimeSpan consoleBufferTimeout, bool autoStart)
        {
            bool consoleEnabled = environment.GetEnvironmentVariable(EnvironmentSettingNames.ConsoleLoggingDisabled) != "1";

            if (consoleEnabled)
            {
                // We are going to used stdout, but do we write directly or use a buffer?
                _consoleBuffer = environment.GetEnvironmentVariable(EnvironmentSettingNames.ConsoleLoggingBufferSize) switch
                {
                    "-1" => Channel.CreateUnbounded<string>(),              // buffer size of -1 indicates that buffer should be enabled but unbounded
                    "0" => null,                                            // buffer size of 0 indicates that buffer should be disabled
                    var s when int.TryParse(s, out int i) && i > 0 => Channel.CreateBounded<string>(i),
                    _ => Channel.CreateBounded<string>(DefaultBufferSize),  // default behavior is to use buffer with default size
                };

                if (_consoleBuffer == null)
                {
                    _writeEvent = Console.WriteLine;
                }
                else
                {
                    _writeEvent = WriteToConsoleBuffer;
                    _consoleBufferTimeout = consoleBufferTimeout;
                    if (autoStart)
                    {
                        bool batched = environment.GetEnvironmentVariable(EnvironmentSettingNames.ConsoleLoggingBufferBatched) switch
                        {
                            "0" => false,     // disable batching by setting to 0
                            _ => true,        // default behavior is batched
                        };

                        StartProcessingBuffer(batched);
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
            try
            {
                while (_consoleBuffer.Writer.TryWrite(evt) == false)
                {
                    // Buffer is currently full, wait until writing is permitted.
                    // This is the downside of using channels, we are on a sync code path and so we have to block on this task
                    var writeTask = _consoleBuffer.Writer.WaitToWriteAsync().AsTask();
                    if (writeTask.WaitAsync(_consoleBufferTimeout).Result == false)
                    {
                        // The buffer has been completed and does not allow further writes.
                        // Currently this should not be possible, but the safest thing to do is just write directly to the console.
                        Console.WriteLine(evt);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                // Most likely a task cancellation exception from the timeout expiring, but regardless we use the handler
                // dump the raw exception and write the event to the console directly
                _exceptionhandler(ex);
                Console.WriteLine(evt);
            }
        }

        internal void StartProcessingBuffer(bool batched)
        {
            // intentional no-op if the task is already running
            if (_consoleBufferReadLoop == null || _consoleBufferReadLoop.IsCompleted)
            {
                _consoleBufferReadLoop = ProcessConsoleBufferAsync(batched);
            }
        }

        private async Task ProcessConsoleBufferAsync(bool batched)
        {
            try
            {
                if (batched)
                {
                    await ProcessConsoleBufferBatchedAsync();
                }
                else
                {
                    await ProcessConsoleBufferNonBatchedAsync();
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
                _consoleBuffer.Writer.Complete();
            }
        }

        private async Task ProcessConsoleBufferNonBatchedAsync()
        {
            await foreach (var line in _consoleBuffer.Reader.ReadAllAsync())
            {
                Console.WriteLine(line);
            }
        }

        private async Task ProcessConsoleBufferBatchedAsync()
        {
            var builder = new StringBuilder();

            while (true)
            {
                if (await _consoleBuffer.Reader.WaitToReadAsync() == false)
                {
                    // The buffer has been completed and does not allow further reads.
                    // Currently this should not be possible, but safest thing to do is break out of the loop.
                    break;
                }

                if (_consoleBuffer.Reader.TryRead(out string line1))
                {
                    // Can we synchronously read multiple lines?
                    // If yes, use the string builder to batch them together into a single write
                    // If no, just write the single line without using the builder;
                    if (_consoleBuffer.Reader.TryRead(out string line2))
                    {
                        builder.AppendLine(line1);
                        builder.AppendLine(line2);
                        int lines = 2;

                        while (_consoleBuffer.Reader.TryRead(out string nextLine) && lines < DefaultBufferSize)
                        {
                            builder.AppendLine(nextLine);
                            lines++;
                        }

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
    }
}
