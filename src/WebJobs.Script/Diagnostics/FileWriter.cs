// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Timers;
using Microsoft.Azure.WebJobs.Script.Config;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class FileWriter : IDisposable
    {
        internal const int LastModifiedCutoffDays = 1;
        internal const long MaxLogFileSizeBytes = 5 * 1024 * 1024;
        internal const int LogFlushIntervalMs = 1000;
        internal const int MaxLogLinesPerFlushInterval = 250;
        private readonly string _logFilePath;
        private readonly string _instanceId;

        private readonly DirectoryInfo _logDirectory;
        private static object _syncLock = new object();
        private FileInfo _currentLogFileInfo;
        private bool _disposed = false;

        private Timer _flushTimer;
        private ConcurrentQueue<string> _logBuffer = new ConcurrentQueue<string>();

        public FileWriter(string logFilePath)
        {
            _logFilePath = logFilePath;
            _instanceId = GetInstanceId();
            _logDirectory = new DirectoryInfo(logFilePath);

            // start a timer to flush accumulated logs in batches
            _flushTimer = new Timer
            {
                AutoReset = true,
                Interval = LogFlushIntervalMs
            };
            _flushTimer.Elapsed += OnFlushLogs;
            _flushTimer.Start();
        }

        public void Flush()
        {
            if (_logBuffer.Count == 0)
            {
                return;
            }

            ConcurrentQueue<string> currentBuffer = null;
            lock (_syncLock)
            {
                // Snapshot the current set of buffered logs
                // and set a new queue. This ensures that any new
                // logs are written to the new buffer.
                // We do this snapshot in a lock since Flush might be
                // called by multiple threads concurrently, and we need
                // to ensure we only log each log once.
                currentBuffer = _logBuffer;
                _logBuffer = new ConcurrentQueue<string>();
            }

            if (currentBuffer.Count == 0)
            {
                return;
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

        private static void AppendToFile(FileInfo fileInfo, string content)
        {
            lock (_syncLock)
            {
                var fs = fileInfo.Open(FileMode.Open, FileAccess.Write, FileShare.Read);
                fs.Seek(0, SeekOrigin.End);

                using (var sw = new StreamWriter(fs))
                {
                    sw.Write(content);
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

        public void AppendLine(string line)
        {
            if (line == null)
            {
                return;
            }

            if (_logBuffer.Count > MaxLogLinesPerFlushInterval)
            {
                return;
            }

            if (_logBuffer.Count == MaxLogLinesPerFlushInterval)
            {
                _logBuffer.Enqueue("Log output threshold exceeded.");
                return;
            }

            // add the line to the current buffer batch, which is flushed
            // on a timer
            _logBuffer.Enqueue(line);
        }

        private void OnFlushLogs(object sender, ElapsedEventArgs e)
        {
            Flush();
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
                lock (_syncLock)
                {
                    // we perform any file create operations in a lock to avoid
                    // race conditions with multiple instances of this class on
                    // multiple threads, where multiple log files might be created
                    // unnecessarily
                    logFile = GetCurrentLogFile();
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
                }
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