using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Azure.WebJobs.Host;

namespace Microsoft.Azure.WebJobs.Script
{
    public class FileTraceWriter : TraceWriter
    {
        private object _syncLock = new object();
        private readonly string _logFilePath;
        private readonly string _logFileName;

        public FileTraceWriter(string logFilePath, TraceLevel level): base (level)
        {
            _logFilePath = logFilePath;
            _logFileName = Path.Combine(_logFilePath, string.Format("{0}.log", Guid.NewGuid()));
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
            // TODO: Once the log file exceeds a certain size, we want to create a new one

            line = string.Format("{0} {1}\r\n", DateTime.Now.ToString("s"), line.Trim());

            // TODO: fix this locking issue
            try
            {
                lock (_syncLock)
                {
                    File.AppendAllText(_logFileName, line);
                }
            }
            catch (DirectoryNotFoundException)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logFileName));
                lock (_syncLock)
                {
                    File.AppendAllText(_logFileName, line);
                }
            }
        }
    }
}
