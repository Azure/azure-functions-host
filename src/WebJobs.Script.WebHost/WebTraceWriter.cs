using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Azure.WebJobs.Host;

namespace WebJobs.Script.WebHost
{
    public class WebTraceWriter : TraceWriter
    {
        private object _syncLock = new object();
        private readonly string _logFilePath;

        public WebTraceWriter(string logFilePath) : base (TraceLevel.Verbose)
        {
            _logFilePath = logFilePath;

            // TODO: figure out the correct path + file name structure
            Directory.CreateDirectory(_logFilePath);
            _logFilePath = Path.Combine(_logFilePath, "log.txt");
        }

        public override void Trace(TraceEvent traceEvent)
        {
            // TODO: buffer logs and write only periodically
            string traceLine = string.Format("{0} {1} {2}\r\n", DateTime.Now.ToString("s"), traceEvent.Level, traceEvent.Message);
            if (traceEvent.Exception != null)
            {
                traceLine += " " + traceEvent.Exception.ToString();
            }

            // TODO: fix this locking issue
            lock (_syncLock)
            {
                File.AppendAllText(_logFilePath, traceLine);
            }
        }
    }
}