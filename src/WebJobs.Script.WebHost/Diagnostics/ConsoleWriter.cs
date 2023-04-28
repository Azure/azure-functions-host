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
        private readonly Channel<string> _consoleBuffer;
        private readonly TimeSpan _consoleBufferTimeout;
        private readonly Task _consoleBufferReadLoop;
        private readonly Action<string> _writeEvent;
        private readonly Action<Exception> _unhandledExceptionHandler;

        public ConsoleWriter(IEnvironment environment, Action<Exception> unhandledExceptionHandler)
            : this(environment, unhandledExceptionHandler, consoleBufferTimeout: TimeSpan.FromSeconds(1), autoStart: true)
        {
        }

        internal ConsoleWriter(IEnvironment environment, Action<Exception> unhandledExceptionHandler, TimeSpan consoleBufferTimeout, bool autoStart)
        {
            bool consoleEnabled = environment.GetEnvironmentVariable(EnvironmentSettingNames.ConsoleLoggingDisabled) != "1";

            if (consoleEnabled)
            {
                // We are going to used stdout, but do we write directly or use a buffer?
                _consoleBuffer = environment.GetEnvironmentVariable(EnvironmentSettingNames.ConsoleLoggingBufferSize) switch
                {
                    "-1" => Channel.CreateUnbounded<string>(), // buffer size of -1 indicates that buffer should be enabled but unbounded
                    var s when int.TryParse(s, out int i) && i > 0 => Channel.CreateBounded<string>(i),
                    _ => null // do not buffer in all other cases
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
                        _consoleBufferReadLoop = environment.GetEnvironmentVariable(EnvironmentSettingNames.ConsoleLoggingBufferBatched) switch
                        {
                            "1" => ProcessConsoleBufferBatchedAsync(),
                            _ => ProcessConsoleBufferAsync()
                        };
                    }
                }
            }
            else
            {
                _writeEvent = (string s) => { };
            }

            _unhandledExceptionHandler = unhandledExceptionHandler;
        }

        public Action<string> WriteHandler => _writeEvent;

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
                // Most likely a task cancellation exception from the timeout expiring, but regardless we handle it the same way:
                // dump the raw exception and write the event to the console directly
                _unhandledExceptionHandler(ex);
                Console.WriteLine(evt);
            }
        }

        internal async Task ProcessConsoleBufferAsync()
        {
            await foreach (var line in _consoleBuffer.Reader.ReadAllAsync())
            {
                Console.WriteLine(line);
            }
        }

        internal async Task ProcessConsoleBufferBatchedAsync()
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

                        while (_consoleBuffer.Reader.TryRead(out string nextLine))
                        {
                            builder.AppendLine(nextLine);
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
