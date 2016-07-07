// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using Microsoft.Azure.WebJobs.Script.WebHost.Kudu;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Streams
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix")]
    public class LogStream
    {
        private static readonly string[] LogFileExtensions = new string[] { ".txt", ".log", ".htm" };
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(1);

        private readonly object _thisLock = new object();
        private readonly ITracer _tracer;

        private Dictionary<string, long> _logFiles;
        private FileSystemWatcher _watcher;
        private Timer _heartbeat;
        private DateTime _lastTraceTime = DateTime.UtcNow;
        private DateTime _startTime = DateTime.UtcNow;
        private TimeSpan _timeout = TimeSpan.FromMinutes(30);
        private string _filter;
        private StreamWriter _stream;
        private string _path;

        public LogStream(string path, string filter, ITracer tracer)
        {
            _path = path;
            _tracer = tracer;
            _filter = filter;
        }

        private void Initialize(string path)
        {
            FileSystemWatcher watcher = new FileSystemWatcher(path);
            watcher.Changed += new FileSystemEventHandler(DoSafeAction<object, FileSystemEventArgs>(OnChanged, "LogStreamManager.OnChanged"));
            watcher.Deleted += new FileSystemEventHandler(DoSafeAction<object, FileSystemEventArgs>(OnDeleted, "LogStreamManager.OnDeleted"));
            watcher.Renamed += new RenamedEventHandler(DoSafeAction<object, RenamedEventArgs>(OnRenamed, "LogStreamManager.OnRenamed"));
            watcher.Error += new ErrorEventHandler(DoSafeAction<object, ErrorEventArgs>(OnError, "LogStreamManager.OnError"));
            watcher.IncludeSubdirectories = true;
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;

            if (_heartbeat == null)
            {
                _heartbeat = new Timer(OnHeartbeat, null, HeartbeatInterval, HeartbeatInterval);
            }

            if (_logFiles == null)
            {
                var logFiles = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                foreach (var ext in LogFileExtensions)
                {
                    foreach (var file in Directory.GetFiles(path, "*" + ext, SearchOption.AllDirectories))
                    {
                        try
                        {
                            logFiles[file] = new FileInfo(file).Length;
                        }
                        catch (Exception ex)
                        {
                            // avoiding racy with providers cleaning up log file
                            _tracer.TraceError(ex);
                        }
                    }
                }

                _logFiles = logFiles;
            }
        }

        public void SetStream(Stream outputStream, HttpContent content, TransportContext context)
        {
            _stream = new StreamWriter(outputStream);
            Initialize(_path);
            _stream.Write(String.Format(CultureInfo.CurrentCulture, "{0}  Welcome, you are now connected to log-streaming service.{1}.", DateTime.UtcNow.ToString("s"), Environment.NewLine));
            _stream.AutoFlush = true;
        }

        private void OnHeartbeat(object state)
        {
            try
            {
                try
                {
                    TimeSpan ts = DateTime.UtcNow.Subtract(_startTime);
                    if (ts >= _timeout)
                    {
                        TerminateClient(String.Format(CultureInfo.CurrentCulture, "{0}  Stream terminated due to timeout {1} min(s).{2}", DateTime.UtcNow.ToString("s"), (int)ts.TotalMinutes, System.Environment.NewLine));
                    }
                    else
                    {
                        ts = DateTime.UtcNow.Subtract(_lastTraceTime);
                        if (ts >= HeartbeatInterval)
                        {
                            NotifyClient(String.Format(CultureInfo.CurrentCulture, "{0}  No new trace in the past {1} min(s).{2}", DateTime.UtcNow.ToString("s"), (int)ts.TotalMinutes, System.Environment.NewLine));
                        }
                    }
                }
                catch (Exception ex)
                {
                    using (_tracer.Step("LogStreamManager.OnHeartbeat"))
                    {
                        _tracer.TraceError(ex);
                    }
                }
            }
            catch
            {
                // no-op
            }
        }

        // Suppress exception on callback to not crash the process.

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            if (e.ChangeType == WatcherChangeTypes.Changed && MatchFilters(e.FullPath))
            {
                // reading the delta of file changed, retry if failed.
                IEnumerable<string> lines = null;
                OperationManager.Attempt(() =>
                {
                    lines = GetChanges(e);
                }, 3, 100);

                if (lines.Count() > 0)
                {
                    _lastTraceTime = DateTime.UtcNow;

                    NotifyClient(lines);
                }
            }
        }

        private void OnDeleted(object sender, FileSystemEventArgs e)
        {
            if (e.ChangeType == WatcherChangeTypes.Deleted)
            {
                lock (_thisLock)
                {
                    _logFiles.Remove(e.FullPath);
                }
            }
        }

        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            if (e.ChangeType == WatcherChangeTypes.Renamed)
            {
                lock (_thisLock)
                {
                    _logFiles.Remove(e.OldFullPath);
                }
            }
        }

        private void OnError(object sender, ErrorEventArgs e)
        {
            using (_tracer.Step("FileSystemWatcher.OnError"))
            {
                _tracer.TraceError(e.GetException());
            }

            try
            {
                lock (_thisLock)
                {
                    if (_watcher != null)
                    {
                        string path = _watcher.Path;
                        Reset();
                        Initialize(path);
                    }
                }
            }
            catch (Exception ex)
            {
                OnCriticalError(ex);
            }
        }

        private void TerminateClient(string text)
        {
            NotifyClient(text);

            lock (_thisLock)
            {
                this._stream.Close();
                // Proactively cleanup resources
                Reset();
            }
        }

        // this has the same performance and implementation as StreamReader.ReadLine()
        // they both account for '\n' or '\r\n' as new line chars.  the difference is 
        // this returns the result with preserved new line chars.
        // without this, logstream can only guess whether it is '\n' or '\r\n' which is 
        // subjective to each log providers/files.
        private static string ReadLine(StreamReader reader)
        {
            var strb = new StringBuilder();
            int val;
            while ((val = reader.Read()) >= 0)
            {
                char ch = (char)val;
                strb.Append(ch);
                switch (ch)
                {
                    case '\r':
                    case '\n':
                        if (ch == '\r' && (char)reader.Peek() == '\n')
                        {
                            ch = (char)reader.Read();
                            strb.Append(ch);
                        }
                        return strb.ToString();
                    default:
                        break;
                }
            }

            return strb.ToString();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Reset();
            }
        }

        private void OnCriticalError(Exception ex)
        {
            TerminateClient(String.Format(CultureInfo.CurrentCulture, "{0}{1}  Error has occurred and stream is terminated. {2}{0}", System.Environment.NewLine, DateTime.UtcNow.ToString("s"), ex.Message));
        }

        private static bool MatchFilters(string fileName)
        {
            if (!string.IsNullOrEmpty(fileName))
            {
                foreach (string ext in LogFileExtensions)
                {
                    if (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void NotifyClient(string text)
        {
            NotifyClient(new string[] { text });
        }

        private void NotifyClient(IEnumerable<string> lines)
        {
            lock (_thisLock)
            {
                try
                {
                    foreach (var line in lines)
                    {
                        _stream.Write(line);
                    }
                }
                catch (Exception)
                {
                    _stream.Close();
                }
            }
        }

        private IEnumerable<string> GetChanges(FileSystemEventArgs e)
        {
            lock (_thisLock)
            {
                long offset = 0;
                if (!_logFiles.TryGetValue(e.FullPath, out offset))
                {
                    _logFiles[e.FullPath] = 0;
                }

                using (FileStream fs = new FileStream(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long length = fs.Length;

                    // file was truncated
                    if (offset > length)
                    {
                        _logFiles[e.FullPath] = offset = 0;
                    }

                    // multiple events
                    if (offset == length)
                    {
                        return Enumerable.Empty<string>();
                    }

                    if (offset != 0)
                    {
                        fs.Seek(offset, SeekOrigin.Begin);
                    }

                    List<string> changes = new List<string>();

                    StreamReader reader = new StreamReader(fs);
                    while (!reader.EndOfStream)
                    {
                        string line = ReadLine(reader);
                        if (String.IsNullOrEmpty(_filter) || line.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            changes.Add(line);
                        }

                        offset += line.Length;
                    }

                    // Adjust offset and return changes
                    _logFiles[e.FullPath] = offset;

                    return changes;
                }
            }
        }

        private void Reset()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                // dispose is blocked till all change request handled, 
                // this could lead to deadlock as we share the same lock
                // http://stackoverflow.com/questions/73128/filesystemwatcher-dispose-call-hangs
                // in the meantime, let GC handle it
                // _watcher.Dispose();
                _watcher = null;
            }

            if (_heartbeat != null)
            {
                _heartbeat.Dispose();
                _heartbeat = null;
            }

            _logFiles = null;
        }

        private Action<T1, T2> DoSafeAction<T1, T2>(Action<T1, T2> func, string eventName)
        {
            return (t1, t2) =>
            {
                try
                {
                    try
                    {
                        func(t1, t2);
                    }
                    catch (Exception ex)
                    {
                        using (_tracer.Step(eventName))
                        {
                            _tracer.TraceError(ex);
                        }
                    }
                }
                catch
                {
                    // no-op
                }
            };
        }
    }
}