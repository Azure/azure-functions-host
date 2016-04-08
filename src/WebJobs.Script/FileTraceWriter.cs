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
using System.Timers;
using Microsoft.Azure.WebJobs.Host;

namespace Microsoft.Azure.WebJobs.Script
{
    public class FileTraceWriter : TraceWriter, IDisposable
    {
        internal const int LastModifiedCutoffDays = 1;
        private const long MaxLogFileSizeBytes = 5 * 1024 * 1024;
        private const int LogFlushIntervalMs = 1000;
        private readonly string _logFilePath;
        private readonly string _instanceId;
        private readonly DirectoryInfo _logDirectory;
        private static object _syncLock = new object();
        private FileInfo _currentLogFileInfo;
        private bool _disposed = false;

        private Timer _flushTimer;
        private ConcurrentQueue<string> _logBuffer = new ConcurrentQueue<string>();

        public FileTraceWriter(string logFilePath, TraceLevel level) : base(level)
        {
            _logFilePath = logFilePath;
            _instanceId = GetInstanceId();

            _logDirectory = new DirectoryInfo(logFilePath);
            if (!_logDirectory.Exists)
            {
                _logDirectory.Create();
            }
            else
            {
                // query for all existing log files for this instance
                // sorted by date, and get the last log file written to (or null)
                var files = GetLogFiles(_logDirectory);
                _currentLogFileInfo = files.FirstOrDefault();
            }

            if (_currentLogFileInfo == null)
            {
                SetNewLogFile();
            }

            // start a timer to flush accumulated logs in batches
            _flushTimer = new Timer
            {
                AutoReset = true,
                Interval = LogFlushIntervalMs
            };
            _flushTimer.Elapsed += OnFlushLogs;
            _flushTimer.Start();
        }

        public override void Flush()
        {
            if (_logBuffer.Count == 0)
            {
                return;
            }

            // snapshot the current set of buffered logs
            // and set a new bag
            ConcurrentQueue<string> currentBuffer = _logBuffer;
            _logBuffer = new ConcurrentQueue<string>();

            // concatenate all lines into one string
            StringBuilder sb = new StringBuilder();
            string line = null;
            while (currentBuffer.TryDequeue(out line))
            {
                sb.AppendLine(line);
            }

            // write all lines in a single file operation
            string contents = sb.ToString();
            try
            {
                lock (_syncLock)
                {
                    File.AppendAllText(_currentLogFileInfo.FullName, contents);
                }
            }
            catch (DirectoryNotFoundException)
            {
                // need to handle cases where log file directories might
                // have been deleted from underneath us
                Directory.CreateDirectory(_logFilePath);
                lock (_syncLock)
                {
                    File.AppendAllText(_currentLogFileInfo.FullName, contents);
                }
            }

            _currentLogFileInfo.Refresh();
            if (_currentLogFileInfo.Length > MaxLogFileSizeBytes)
            {
                SetNewLogFile();
            }
        }

        public override void Trace(TraceEvent traceEvent)
        {
            if (traceEvent == null)
            {
                throw new ArgumentNullException("traceEvent");
            }

            AppendLine(traceEvent.Message);
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
                    AppendLine(actualException.Message);
                }
                else
                {
                    AppendLine(traceEvent.Exception.ToString());
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_flushTimer != null)
                    {
                        _flushTimer.Dispose();
                    }

                    // ensure any remaining logs are flushed
                    Flush();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void AppendLine(string line)
        {
            if (line == null)
            {
                return;
            }

            // add the line to the current buffer batch, which is flushed
            // on a timer
            line = string.Format(CultureInfo.InvariantCulture, "{0} {1}", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture), line.Trim());
            _logBuffer.Enqueue(line);
        }

        private void OnFlushLogs(object sender, ElapsedEventArgs e)
        {
            Flush();
        }

        internal void SetNewLogFile()
        {
            // purge any log files (regardless of instance ID) whose last write time was earlier
            // than our retention policy
            var filesToPurge = _logDirectory.GetFiles("*.log").Where(p => (DateTime.Now - p.LastWriteTime).TotalDays > LastModifiedCutoffDays);
            DeleteFiles(filesToPurge);

            // we include a machine identifier in the log file name to ensure we don't have any
            // log file contention between scaled out instances
            string filePath = Path.Combine(_logFilePath, string.Format(CultureInfo.InvariantCulture, "{0}-{1}.log", Guid.NewGuid(), _instanceId));
            _currentLogFileInfo = new FileInfo(filePath);
        }

        private IEnumerable<FileInfo> GetLogFiles(DirectoryInfo directory)
        {
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
            string instanceId = System.Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID");
            if (string.IsNullOrEmpty(instanceId))
            {
                instanceId = Environment.MachineName;
            }

            instanceId = instanceId.Length > 10 ? instanceId.Substring(0, 10) : instanceId;

            return instanceId.ToLowerInvariant();
        }
    }
}
