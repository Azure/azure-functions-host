// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal class ConsoleConcurrentQueueLogger
    {
        private static readonly TimeSpan DefaultConsoleBufferTimeout = TimeSpan.FromSeconds(1);
        private readonly ConcurrentQueue<string> _consoleBuffer;
        private readonly TimeSpan _consoleBufferTimeout;
        private readonly Action<Exception> _exceptionhandler;
        private readonly int _bufferSizeSoftLimit;
        private Thread _consoleBufferReadLoop;
        private Action<string> _writeEvent;
        private ManualResetEvent _writeResetEvent;
        private ManualResetEvent _readResetEvent;

        public ConsoleConcurrentQueueLogger(IEnvironment environment, Action<Exception> unhandledExceptionHandler)
            : this(environment, unhandledExceptionHandler, consoleBufferTimeout: DefaultConsoleBufferTimeout, autoStart: true)
        {
        }

        internal ConsoleConcurrentQueueLogger(IEnvironment environment, Action<Exception> exceptionHandler, TimeSpan consoleBufferTimeout, bool autoStart)
        {
            _bufferSizeSoftLimit = environment.GetEnvironmentVariable(EnvironmentSettingNames.ConsoleLoggingBufferSize) switch
            {
                var s when int.TryParse(s, out int i) && i > 0 => i,
                _ => 8000
            };

            _consoleBuffer = new ConcurrentQueue<string>();
            _exceptionhandler = exceptionHandler;
            _consoleBufferTimeout = consoleBufferTimeout;

            _writeResetEvent = new ManualResetEvent(true);
            _readResetEvent = new ManualResetEvent(true);

            _writeEvent = WriteToConsoleBuffer;
            if (autoStart)
            {
                StartProcessingBuffer();
            }
        }

        public void WriteHandler(string evt)
        {
            _writeEvent(evt);
        }

        private void WriteToConsoleBuffer(string evt)
        {
            if (_consoleBuffer.Count >= _bufferSizeSoftLimit)
            {
                _readResetEvent.Set();
                _writeResetEvent.Reset();
                if (!_writeResetEvent.WaitOne(_consoleBufferTimeout))
                {
                    Console.WriteLine(evt);
                    return;
                }
            }

            _consoleBuffer.Enqueue(evt);
            _readResetEvent.Set();
        }

        internal void StartProcessingBuffer()
        {
            // intentional no-op if the thread is already running
            if (_consoleBufferReadLoop == null)
            {
                _consoleBufferReadLoop = new Thread(ProcessConsoleBuffer)
                {
                    IsBackground = true,
                };
                _consoleBufferReadLoop.Start();
            }
        }

        private void ProcessConsoleBuffer()
        {
            try
            {
                ProcessConsoleBufferBatched();
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
            }
        }

        private void ProcessConsoleBufferBatched()
        {
            var builder = new StringBuilder();

            while (true)
            {
                if (_consoleBuffer.TryDequeue(out string line1))
                {
                    _writeResetEvent.Set();

                    // Can we synchronously read multiple lines?
                    // If yes, use the string builder to batch them together into a single write
                    // If no, just write the single line without using the builder;
                    if (_consoleBuffer.TryDequeue(out string line2))
                    {
                        builder.AppendLine(line1);
                        builder.AppendLine(line2);
                        int lines = 2;
                        while (lines < ConsoleWriter.DefaultBufferSize && _consoleBuffer.TryDequeue(out string nextLine))
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
                else
                {
                    _readResetEvent.Reset();
                    _readResetEvent.WaitOne();
                }
            }
        }
    }
}
