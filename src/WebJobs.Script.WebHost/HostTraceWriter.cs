using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Azure.WebJobs.Host;

namespace WebJobs.Script.WebHost
{
    public class HostTraceWriter : TraceWriter
    {
        private object _syncLock = new object();
        private readonly string _rootLogPath;
        private readonly string _hostLogFilePath;

        public HostTraceWriter(string logFilePath) : base (TraceLevel.Verbose)
        {
            _rootLogPath = logFilePath;

            // TODO: figure out the correct path + file name structure
            Directory.CreateDirectory(_rootLogPath);
            _hostLogFilePath = Path.Combine(_rootLogPath, "host.log");
        }

        public override void Trace(TraceEvent traceEvent)
        {
            // TODO: figure out the right log file format
            // TODO: buffer logs and write only periodically
            string traceLine = string.Format("{0} {1} {2}\r\n", DateTime.Now.ToString("s"), traceEvent.Level, traceEvent.Message);
            if (traceEvent.Exception != null)
            {
                traceLine += " " + traceEvent.Exception.ToString();
            }

            // TODO: fix this locking issue
            lock (_syncLock)
            {
                File.AppendAllText(_hostLogFilePath, traceLine);
            }
        }
    }
}