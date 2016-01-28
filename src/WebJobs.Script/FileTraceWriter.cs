using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Host;

namespace Microsoft.Azure.WebJobs.Script
{
    public class FileTraceWriter : TraceWriter
    {
        private object _syncLock = new object();
        private readonly string _logFilePath;
        private const long _maxLogFileSizeBytes = 5 * 1024 * 1024;
        private FileInfo _currentLogFileInfo;

        public FileTraceWriter(string logFilePath, TraceLevel level): base (level)
        {
            _logFilePath = logFilePath;

            DirectoryInfo directory = new DirectoryInfo(logFilePath);
            if (!directory.Exists)
            {
                Directory.CreateDirectory(logFilePath);
            }
            else
            {
                // get the last log file written to (or null)
                _currentLogFileInfo = directory.GetFiles().OrderByDescending(p => p.LastWriteTime).FirstOrDefault();
            }

            if (_currentLogFileInfo == null)
            {
                SetNewLogFile();
            }
        }

        public override void Trace(TraceEvent traceEvent)
        {
            // TODO: figure out the right log file format
            // TODO: buffer logs and write only periodically
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

        protected virtual void AppendLine(string line)
        {
            line = string.Format("{0} {1}\r\n", DateTime.Now.ToString("s"), line.Trim());

            // TODO: fix this locking issue
            lock (_syncLock)
            {
                File.AppendAllText(_currentLogFileInfo.FullName, line);
            }

            // TODO: Need to optimize this, so we only do the check every
            // so often
            _currentLogFileInfo.Refresh();
            if (_currentLogFileInfo.Length > _maxLogFileSizeBytes)
            {
                SetNewLogFile();
            }
        }

        private void SetNewLogFile()
        {
            string filePath = Path.Combine(_logFilePath, string.Format("{0}.log", Guid.NewGuid()));
            _currentLogFileInfo = new FileInfo(filePath);
        }
    }
}
