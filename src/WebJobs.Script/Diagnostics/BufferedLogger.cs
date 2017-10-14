// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    // Abstract class that buffers the traces and flushes them periodically
    // Methods of this class are thread safe.
    // Subclass this class to provide custom buffered tracing implementation. Example: Trace to sql, trace to file, etc
    public abstract class BufferedLogger : ILogger, IDisposable
    {
        private const int LogFlushIntervalMs = 1000;
        private readonly object _syncLock = new object();
        private readonly Timer _flushTimer;

        private string _category;
        private ConcurrentQueue<TraceMessage> _logBuffer = new ConcurrentQueue<TraceMessage>();

        protected BufferedLogger(string categoryName)
        {
            try
            {
                _category = categoryName;

                _flushTimer = new Timer(LogFlushIntervalMs);

                // start a timer to flush accumulated logs in batches
                _flushTimer.AutoReset = true;
                _flushTimer.Elapsed += OnFlushLogs;
                _flushTimer.Start();
            }
            catch
            {
                // Clean up, if the constructor throws
                if (_flushTimer != null)
                {
                    _flushTimer.Dispose();
                }

                throw;
            }
        }

        ~BufferedLogger()
        {
            Dispose(false);
        }

        public string Category
        {
            get
            {
                return _category;
            }
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            IEnumerable<KeyValuePair<string, object>> properties = state as IEnumerable<KeyValuePair<string, object>>;
            string formattedMessage = formatter?.Invoke(state, exception);

            AppendLine(formattedMessage);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            return DictionaryLoggerScope.Push(state);
        }

        public void Flush()
        {
            if (_logBuffer.Count == 0)
            {
                return;
            }

            ConcurrentQueue<TraceMessage> currentBuffer;
            lock (_syncLock)
            {
                // Snapshot the current set of buffered logs
                // and set a new queue. This ensures that any new
                // logs are written to the new buffer.
                // We do this snapshot in a lock since Flush might be
                // called by multiple threads concurrently, and we need
                // to ensure we only log each log once.
                currentBuffer = _logBuffer;
                _logBuffer = new ConcurrentQueue<TraceMessage>();
            }

            if (currentBuffer.Count == 0)
            {
                return;
            }

            // Flush the trace messages
            lock (_syncLock)
            {
                this.FlushAsync(currentBuffer).Wait();
            }
        }

        // Flush the traces
        protected abstract Task FlushAsync(IEnumerable<TraceMessage> traceMessages);

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _flushTimer.Dispose();

                // ensure any remaining logs are flushed
                Flush();
            }
        }

        protected virtual void AppendLine(string line)
        {
            if (line == null)
            {
                return;
            }

            // add the line to the current buffer batch, which is flushed on a timer
            _logBuffer.Enqueue(new TraceMessage
            {
                Time = DateTime.UtcNow,
                Message = line.Trim()
            });
        }

        protected string GetFunctionName()
        {
            IDictionary<string, object> scopeProperties = DictionaryLoggerScope.GetMergedStateDictionary();

            if (!scopeProperties.TryGetValue(ScriptConstants.LoggerFunctionNameKey, out string functionName))
            {
                return null;
            }

            // this function name starts with "Functions.", but file paths do not include this
            string functionsPrefix = "Functions.";
            if (functionName.StartsWith(functionsPrefix))
            {
                functionName = functionName.Substring(functionsPrefix.Length);
            }

            return functionName;
        }

        private void OnFlushLogs(object sender, ElapsedEventArgs e)
        {
            Flush();
        }
    }

    public class TraceMessage
    {
        public DateTime Time { get; set; }

        public string MachineName { get; set; }

        public string AppName { get; set; }

        public string FunctionName { get; set; }

        public string Message { get; set; }
    }
}