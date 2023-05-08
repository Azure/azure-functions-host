// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal class BlockingCollectionConsoleWriter
    {
        private static readonly TimeSpan DefaultConsoleBufferTimeout = TimeSpan.FromSeconds(1);
        private readonly BlockingCollection<string> _consoleBuffer;
        private readonly TimeSpan _consoleBufferTimeout;
        private readonly Action<Exception> _exceptionhandler;
        private Thread _consoleBufferReadLoop;
        private Action<string> _writeEvent;

        public BlockingCollectionConsoleWriter(IEnvironment environment, Action<Exception> unhandledExceptionHandler)
            : this(environment, unhandledExceptionHandler, consoleBufferTimeout: DefaultConsoleBufferTimeout, autoStart: true)
        {
        }

        internal BlockingCollectionConsoleWriter(IEnvironment environment, Action<Exception> exceptionHandler, TimeSpan consoleBufferTimeout, bool autoStart)
        {
            bool consoleEnabled = environment.GetEnvironmentVariable(EnvironmentSettingNames.ConsoleLoggingDisabled) != "1";

            if (consoleEnabled)
            {
                // We are going to used stdout, but do we write directly or use a buffer?
                _consoleBuffer = environment.GetEnvironmentVariable(EnvironmentSettingNames.ConsoleLoggingBufferSize) switch
                {
                    "-1" => new BlockingCollection<string>(new ConcurrentQueue<string>()),
                    "0" => null,                                            // buffer size of 0 indicates that buffer should be disabled
                    var s when int.TryParse(s, out int i) && i > 0 => new BlockingCollection<string>(new ConcurrentQueue<string>(), i),
                    _ => new BlockingCollection<string>(new ConcurrentQueue<string>(), ConsoleWriter.DefaultBufferSize)
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
            if (_consoleBuffer.TryAdd(evt) == false)
            {
                var cts = new CancellationTokenSource();
                cts.CancelAfter(DefaultConsoleBufferTimeout);

                try
                {
                    _consoleBuffer.Add(evt, cts.Token);
                }
                catch (OperationCanceledException cancelledEx)
                {
                    _exceptionhandler(cancelledEx);
                    Console.WriteLine(evt);
                }
            }
        }

        internal void StartProcessingBuffer(bool batched)
        {
            // intentional no-op if the task is already running
            if (_consoleBufferReadLoop == null)
            {
                _consoleBufferReadLoop = new Thread(() => ProcessConsoleBuffer(batched))
                {
                    IsBackground = true,
                };
            }
        }

        private void ProcessConsoleBuffer(bool batched)
        {
            try
            {
                if (batched)
                {
                    ProcessConsoleBufferBatched();
                }
                else
                {
                    ProcessConsoleBufferNonBatched();
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
                _consoleBuffer.CompleteAdding();
            }
        }

        private void ProcessConsoleBufferNonBatched()
        {
            while (true)
            {
                string line = _consoleBuffer.Take();
                Console.WriteLine(line);
            }
        }

        private void ProcessConsoleBufferBatched()
        {
            var builder = new StringBuilder();

            while (true)
            {
                if (_consoleBuffer.TryTake(out string line1))
                {
                    // Can we synchronously read multiple lines?
                    // If yes, use the string builder to batch them together into a single write
                    // If no, just write the single line without using the builder;
                    if (_consoleBuffer.TryTake(out string line2))
                    {
                        builder.AppendLine(line1);
                        builder.AppendLine(line2);
                        int lines = 2;
                        while (_consoleBuffer.TryTake(out string nextLine) && lines < ConsoleWriter.DefaultBufferSize)
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
