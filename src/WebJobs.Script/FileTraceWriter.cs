// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Host;

namespace Microsoft.Azure.WebJobs.Script
{
    public class FileTraceWriter : TraceWriter
    {
        private object _syncLock = new object();
        private readonly string _logFilePath;
        private readonly string _instanceId;
        private const long _maxLogFileSizeBytes = 5 * 1024 * 1024;
        private FileInfo _currentLogFileInfo;

        public FileTraceWriter(string logFilePath, TraceLevel level): base (level)
        {
            _logFilePath = logFilePath;
            _instanceId = GetInstanceId();

            DirectoryInfo directory = new DirectoryInfo(logFilePath);
            if (!directory.Exists)
            {
                Directory.CreateDirectory(logFilePath);
            }
            else
            {
                // get the last log file written to (or null)
                string pattern = string.Format(CultureInfo.InvariantCulture, "*-{0}.log", _instanceId);
                _currentLogFileInfo = directory.GetFiles(pattern).OrderByDescending(p => p.LastWriteTime).FirstOrDefault();
            }

            if (_currentLogFileInfo == null)
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
            if (line == null)
            {
                return;
            }

            // TODO: figure out the right log file format
            line = string.Format(CultureInfo.InvariantCulture, "{0} {1}\r\n", DateTime.Now.ToString("s", CultureInfo.InvariantCulture), line.Trim());

            // TODO: optimize this locking
            try
            {
                lock (_syncLock)
                {
                    File.AppendAllText(_currentLogFileInfo.FullName, line);
                }
            }
            catch (DirectoryNotFoundException)
            {
                // need to handle cases where log file directories might
                // have been deleted from underneath us
                Directory.CreateDirectory(_logFilePath);
                lock (_syncLock)
                {
                    File.AppendAllText(_currentLogFileInfo.FullName, line);
                }
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
            // we include a machine identifier in the log file name to ensure we don't have any
            // log file contention between scaled out instances
            string filePath = Path.Combine(_logFilePath, string.Format(CultureInfo.InvariantCulture, "{0}-{1}.log", Guid.NewGuid(), _instanceId));
            _currentLogFileInfo = new FileInfo(filePath);
        }

        private static string GetInstanceId()
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
