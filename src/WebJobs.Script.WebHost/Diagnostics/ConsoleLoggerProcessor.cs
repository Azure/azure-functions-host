// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal class ConsoleLoggerProcessor : IDisposable
    {
        private readonly Queue<string> _messageQueue;
        private readonly Thread _outputThread;
        private bool _isAddingCompleted;
        private int _maxQueuedMessages;

        public ConsoleLoggerProcessor(IEnvironment environment)
        {
            _maxQueuedMessages = environment.GetEnvironmentVariable(EnvironmentSettingNames.ConsoleLoggingBufferSize) switch
            {
                var s when int.TryParse(s, out int i) && i > 0 => i,
                _ => ConsoleWriter.DefaultBufferSize
            };

            _messageQueue = new Queue<string>();
            // Start Console message queue processor
            _outputThread = new Thread(ProcessLogQueue)
            {
                IsBackground = true,
                Name = "Console logger queue processing thread"
            };
            _outputThread.Start();
        }

        public virtual void EnqueueMessage(string message)
        {
            // cannot enqueue when adding is completed
            if (!Enqueue(message))
            {
                WriteMessage(message);
            }
        }

        // internal for testing
        internal void WriteMessage(string entry)
        {
            try
            {
                Console.Write(entry);
            }
            catch
            {
                CompleteAdding();
            }
        }

        private void ProcessLogQueue()
        {
            while (TryDequeue(out string message))
            {
                WriteMessage(message);
            }
        }

        public bool Enqueue(string item)
        {
            lock (_messageQueue)
            {
                while (_messageQueue.Count >= _maxQueuedMessages && !_isAddingCompleted)
                {
                    Monitor.Wait(_messageQueue);
                }

                if (!_isAddingCompleted)
                {
                    Debug.Assert(_messageQueue.Count < _maxQueuedMessages);
                    bool startedEmpty = _messageQueue.Count == 0;

                    // if we just logged the dropped message warning this could push the queue size to
                    // MaxLength + 1, that shouldn't be a problem. It won't grow any further until it is less than
                    // MaxLength once again.
                    _messageQueue.Enqueue(item);

                    // if the queue started empty it could be at 1 or 2 now
                    if (startedEmpty)
                    {
                        // pulse for wait in Dequeue
                        Monitor.PulseAll(_messageQueue);
                    }

                    return true;
                }
            }

            return false;
        }

        public bool TryDequeue(out string item)
        {
            lock (_messageQueue)
            {
                while (_messageQueue.Count == 0 && !_isAddingCompleted)
                {
                    Monitor.Wait(_messageQueue);
                }

                if (_messageQueue.Count > 0)
                {
                    item = _messageQueue.Dequeue();
                    if (_messageQueue.Count == _maxQueuedMessages - 1)
                    {
                        // pulse for wait in Enqueue
                        Monitor.PulseAll(_messageQueue);
                    }

                    return true;
                }

                item = default;
                return false;
            }
        }

        public void Dispose()
        {
            CompleteAdding();

            try
            {
                _outputThread.Join(1500); // with timeout in-case Console is locked by user input
            }
            catch (ThreadStateException)
            {
            }
        }

        private void CompleteAdding()
        {
            lock (_messageQueue)
            {
                _isAddingCompleted = true;
                Monitor.PulseAll(_messageQueue);
            }
        }
    }
}