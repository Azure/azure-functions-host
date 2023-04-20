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
        private readonly bool _consoleEnabled = true;
        private readonly Channel<string> _consoleBuffer;
        private readonly TimeSpan _consoleBufferTimeout;
        private readonly Task _consoleBufferReadLoop;
        private readonly bool _consoleBufferBatched = false;
        private readonly Action<string> _writeEvent;
        private readonly Action<Exception> _unhandledExceptionHandler;

        public ConsoleWriter(IEnvironment environment, Action<Exception> unhandledExceptionHandler)
            : this(environment, unhandledExceptionHandler, consoleBufferTimeout: TimeSpan.FromSeconds(1), autoStart: true)
        {
        }

        internal ConsoleWriter(IEnvironment environment, Action<Exception> unhandledExceptionHandler, TimeSpan consoleBufferTimeout, bool autoStart)
        {
            if (environment.GetEnvironmentVariable(EnvironmentSettingNames.ConsoleLoggingDisabled) == "1")
            {
                _consoleEnabled = false;
            }

            // We are going to used stdout, but do we write directly or use a buffer?
            _consoleBuffer = environment.GetEnvironmentVariable(EnvironmentSettingNames.ConsoleLoggingBufferSize) switch
            {
                "-1" => Channel.CreateUnbounded<string>(), // buffer size of -1 indicates that buffer should be enabled but unbounded
                var s when int.TryParse(s, out int i) && i > 0 => Channel.CreateBounded<string>(i),
                _ => null // do not buffer in all other cases
            };

            if (_consoleEnabled == false)
            {
                _writeEvent = (string s) => { };
            }
            else if (_consoleBuffer == null)
            {
                _writeEvent = Console.WriteLine;
            }
            else
            {
                _consoleBufferBatched = environment.GetEnvironmentVariable(EnvironmentSettingNames.ConsoleLoggingBufferBatched) switch
                {
                    "1" => true,
                    _ => false
                };

                _writeEvent = WriteToConsoleBuffer;
                _consoleBufferTimeout = consoleBufferTimeout;

                if (autoStart)
                {
                    _consoleBufferReadLoop = ProcessConsoleBufferAsync();
                }
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
            if (_consoleBufferBatched)
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

                    while (_consoleBuffer.Reader.TryRead(out var line))
                    {
                        builder.AppendLine(line);
                    }
                    Console.Write(builder.ToString());
                    builder.Clear();
                }
            }
            else
            {
                await foreach (var line in _consoleBuffer.Reader.ReadAllAsync())
                {
                    Console.WriteLine(line);
                }
            }
        }
    }
}
