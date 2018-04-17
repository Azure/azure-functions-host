// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script
{
    public class FileTraceWriter : TraceWriter, IDisposable
    {
        internal const int LastModifiedCutoffDays = 1;
        internal const long MaxLogFileSizeBytes = 5 * 1024 * 1024;
        internal const int LogFlushIntervalMs = 1000;
        internal const int MaxLogLinesPerFlushInterval = 250;
        private static readonly ConcurrentDictionary<string, object> _flushLocks = new ConcurrentDictionary<string, object>();
        private readonly string _logFilePath;
        private readonly string _instanceId;
        private readonly LogType _logType;

        private readonly DirectoryInfo _logDirectory;
        private readonly object _flushTimerSyncRoot = new object();
        private FileInfo _currentLogFileInfo;
        private bool _disposed = false;
        private Timer _flushTimer;
        private ConcurrentQueue<string> _logBuffer = new ConcurrentQueue<string>();

        public FileTraceWriter(string logFilePath, TraceLevel level, LogType logType) : base(level)
        {
            _logFilePath = logFilePath;
            _instanceId = GetInstanceId();
            _logType = logType;
            _logDirectory = new DirectoryInfo(logFilePath);
        }

        /// <summary>
        /// Maintains a collection of objects used for locking, scoped to the file path.
        /// The objects are not currently removed, but the list is pretty static and shouldn't be a concern.
        /// These cannot be removed on disposal. If cleanup functionality is ever required, we can address this with a scheduled cleanup or something along those lines.
        /// </summary>
        /// <returns>A lock object scoped to the current <see cref="FileTraceWriter"/> log file path.</returns>
        private object GetFlushLock() => _flushLocks.GetOrAdd(_logFilePath, new object());

        private protected virtual void StartFlushTimer()
        {
            if (_flushTimer != null)
            {
                return;
            }

            lock (_flushTimerSyncRoot)
            {
                if (_flushTimer == null)
                {
                    // start a timer to flush accumulated logs in batches
                    _flushTimer = new Timer(FlushCallback, null, LogFlushIntervalMs, LogFlushIntervalMs);
                }
            }
        }

        private protected virtual void StopFlushTimer()
        {
            if (_flushTimer == null)
            {
                return;
            }

            lock (_flushTimerSyncRoot)
            {
                if (_flushTimer != null)
                {
                    _flushTimer.Dispose();
                    _flushTimer = null;

                    // Since we just stopped our timer, we'll perform an explicit
                    // flush to ensure there was nothing written to the buffer
                    Flush();
                }
            }
        }

        private void FlushCallback(object state) => Flush(true);

        public override void Flush() => Flush(false);

        private protected virtual bool Flush(bool timerCallback)
        {
            if (_logBuffer.Count == 0)
            {
                if (timerCallback)
                {
                    StopFlushTimer();
                }

                return true;
            }

            object flushLock = GetFlushLock();

            // If this was an explicit Flush call, we'll block until the lock is acquired;
            // otherwise, we'll return immediately.
            if (!timerCallback)
            {
                Monitor.Enter(flushLock);
            }
            else if (!Monitor.TryEnter(flushLock))
            {
                return false;
            }

            try
            {
                // Snapshot the current set of buffered logs
                // and set a new queue. This ensures that any new
                // logs are written to the new buffer.
                ConcurrentQueue<string> currentBuffer = _logBuffer;
                _logBuffer = new ConcurrentQueue<string>();

                if (currentBuffer.Count == 0)
                {
                    return true;
                }

                // concatenate all lines into one string
                StringBuilder sb = new StringBuilder();
                string line = null;
                while (currentBuffer.TryDequeue(out line))
                {
                    sb.AppendLine(line);
                }

                if (_currentLogFileInfo == null)
                {
                    // delay create log file
                    SetLogFile();
                }

                // write all lines in a single file operation
                string content = sb.ToString();
                try
                {
                    AppendToFile(_currentLogFileInfo, content);
                }
                catch (Exception ex) when (ex is DirectoryNotFoundException || ex is FileNotFoundException)
                {
                    // need to handle cases where log files or directories might
                    // have been deleted from underneath us
                    SetLogFile();
                    AppendToFile(_currentLogFileInfo, content);
                }

                // check to see if we need to roll to a new log file
                _currentLogFileInfo.Refresh();
                if (LogFileIsOverSize(_currentLogFileInfo))
                {
                    SetLogFile();
                }
            }
            finally
            {
                Monitor.Exit(flushLock);
            }

            return true;
        }

        public override void Trace(TraceEvent traceEvent)
        {
            if (traceEvent == null)
            {
                throw new ArgumentNullException(nameof(traceEvent));
            }

            if (traceEvent.Properties.TryGetValue(ScriptConstants.TracePropertyIsSystemTraceKey, out object value)
                && value is bool && (bool)value)
            {
                // we don't want to write system traces to the user trace files
                return;
            }

            if (Level < traceEvent.Level || _logBuffer.Count > MaxLogLinesPerFlushInterval)
            {
                return;
            }

            if (_logBuffer.Count == MaxLogLinesPerFlushInterval)
            {
                AppendLine(traceEvent, "Log output threshold exceeded.");
                return;
            }

            AppendLine(traceEvent, traceEvent.Message);

            if (traceEvent.Exception != null)
            {
                if (traceEvent.Exception is FunctionInvocationException ||
                    traceEvent.Exception is AggregateException)
                {
                    // we want to minimize the stack traces for function invocation
                    // failures, so we drill into the very inner exception, which will
                    // be the script error
                    Exception actualException = traceEvent.Exception;
                    while (actualException.InnerException != null)
                    {
                        actualException = actualException.InnerException;
                    }
                    AppendLine(traceEvent, actualException.Message);
                }
                else
                {
                    AppendLine(traceEvent, traceEvent.Exception.ToFormattedString());
                }
            }

            // Start our flush timer
            StartFlushTimer();
        }

        internal static string GetTracePrefix(TraceEvent traceEvent, LogType logType)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(traceEvent.Level.ToString());

            object value = null;
            if (logType == LogType.Host && traceEvent.Properties.TryGetValue(ScriptConstants.TracePropertyFunctionNameKey, out value))
            {
                sb.AppendFormat(",{0}",  value);
            }

            return sb.ToString();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Stop our timer
                    // This will also flush if there's anything buffered.
                    StopFlushTimer();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void AppendLine(TraceEvent traceEvent, string line)
        {
            if (line == null)
            {
                return;
            }

            // add the line to the current buffer batch, which is flushed
            // on a timer
            line = FormatLine(traceEvent, line);

            _logBuffer.Enqueue(line);
        }

        private static void AppendToFile(FileInfo fileInfo, string content)
        {
            var fs = fileInfo.Open(FileMode.Open, FileAccess.Write, FileShare.Write);
            fs.Seek(0, SeekOrigin.End);

            using (var sw = new StreamWriter(fs))
            {
                sw.Write(content);
            }
        }

        /// <summary>
        /// Format the log line for the current event being traced.
        /// </summary>
        /// <param name="traceEvent">The event currently being traced.</param>
        /// <param name="line">The log line to format.</param>
        /// <returns>The formatted log message.</returns>
        protected virtual string FormatLine(TraceEvent traceEvent, string line)
        {
            string tracePrefix = GetTracePrefix(traceEvent, _logType);
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);
            string formattedLine = $"{timestamp} [{tracePrefix}] {line.Trim()}";

            return formattedLine;
        }

        /// <summary>
        /// Called to set/create the current log file that will be used. Also handles purging of old
        /// log files.
        /// </summary>
        internal void SetLogFile()
        {
            _logDirectory.Refresh();
            if (!_logDirectory.Exists)
            {
                _logDirectory.Create();
            }

            // Check to see if there is already a log file we can use
            bool newLogFileCreated = false;
            var logFile = GetCurrentLogFile();
            if (logFile != null)
            {
                _currentLogFileInfo = logFile;
            }
            else
            {
                // create a new log file
                // we include a machine identifier in the log file name to ensure we don't have any
                // log file contention between scaled out instances
                var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH-mm-ssK");
                string filePath = Path.Combine(_logFilePath, $"{timestamp}-{_instanceId}.log");
                _currentLogFileInfo = new FileInfo(filePath);
                _currentLogFileInfo.Create().Close();
                newLogFileCreated = true;
            }

            if (newLogFileCreated)
            {
                // purge any log files (regardless of instance ID) whose last write time was earlier
                // than our retention policy
                // only do this peridically when we create a new file which will tend to keep
                // this off the startup path
                var filesToPurge = _logDirectory.GetFiles("*.log").Where(p => LogFileIsOld(p));
                DeleteFiles(filesToPurge);
            }
        }

        private static bool LogFileIsOverSize(FileInfo fileInfo)
        {
            // return true if the file is over our size threshold
            return fileInfo.Length > MaxLogFileSizeBytes;
        }

        private static bool LogFileIsOld(FileInfo fileInfo)
        {
            return (DateTime.UtcNow - fileInfo.LastWriteTimeUtc).TotalDays > LastModifiedCutoffDays;
        }

        private FileInfo GetCurrentLogFile()
        {
            // return the last log file written to which is still under
            // size threshold
            return GetLogFiles(_logDirectory).FirstOrDefault(p => !LogFileIsOverSize(p));
        }

        private IEnumerable<FileInfo> GetLogFiles(DirectoryInfo directory)
        {
            // query for all existing log files for this instance
            // sorted by date
            string pattern = string.Format(CultureInfo.InvariantCulture, "*-{0}.log", _instanceId);
            return directory.GetFiles(pattern).OrderByDescending(p => p.LastWriteTime);
        }

        private static void DeleteFiles(IEnumerable<FileInfo> filesToPurge)
        {
            foreach (var file in filesToPurge)
            {
                try
                {
                    file.Delete();
                }
                catch
                {
                    // best effort
                }
            }
        }

        internal static string GetInstanceId()
        {
            string instanceId = ScriptSettingsManager.Instance.GetSetting(EnvironmentSettingNames.AzureWebsiteInstanceId);
            if (string.IsNullOrEmpty(instanceId))
            {
                instanceId = Environment.MachineName;
            }

            instanceId = instanceId.Length > 10 ? instanceId.Substring(0, 10) : instanceId;

            return instanceId.ToLowerInvariant();
        }
    }
}
