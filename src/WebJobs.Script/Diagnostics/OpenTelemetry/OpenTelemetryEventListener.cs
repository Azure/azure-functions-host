// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Timers;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry
{
    internal class OpenTelemetryEventListener : EventListener
    {
        private const int LogFlushIntervalMs = 10 * 1000;
        private const string EventSourceNamePrefix = "OpenTelemetry-";
        private const int MaxLogLinesPerFlushInterval = 35;
        private const string EventName = nameof(OpenTelemetryEventListener);
        private static readonly DiagnosticListener _source = new(ScriptConstants.HostDiagnosticSourcePrefix + "OpenTelemetry");
        private readonly EventLevel _eventLevel;
        private Timer _flushTimer;
        private ConcurrentQueue<string> _logBuffer = new ConcurrentQueue<string>();
        private ConcurrentQueue<EventSource> _eventSource = new ConcurrentQueue<EventSource>();
        private static object _syncLock = new object();
        private bool _disposed = false;

        public OpenTelemetryEventListener(EventLevel eventLevel)
        {
            this._eventLevel = eventLevel;
            _flushTimer = new Timer
            {
                AutoReset = true,
                Interval = LogFlushIntervalMs
            };
            _flushTimer.Elapsed += (sender, e) => Flush();
            _flushTimer.Start();
        }

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name.StartsWith(EventSourceNamePrefix))
            {
                EnableEvents(eventSource, _eventLevel, EventKeywords.All);
                _eventSource.Enqueue(eventSource);
            }
            base.OnEventSourceCreated(eventSource);
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (!string.IsNullOrWhiteSpace(eventData.Message) && !eventData.Message.Contains("will be ignored."))
            {
                _logBuffer.Enqueue(string.Format(CultureInfo.InvariantCulture, eventData.Message, eventData.Payload.ToArray()));
                if (_logBuffer.Count >= MaxLogLinesPerFlushInterval)
                {
                    Flush();
                }
            }
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
                if (_logBuffer.Count == 0)
                {
                    return;
                }
                currentBuffer = _logBuffer;
                _logBuffer = new ConcurrentQueue<string>();
            }

            // batch up to 30 events in one log
            StringBuilder sb = new StringBuilder();
            // start with a new line
            sb.AppendLine(string.Empty);
            while (currentBuffer.TryDequeue(out string line))
            {
                sb.AppendLine(line);
            }
            _source.Write(EventName, sb.ToString());
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    while (_eventSource.TryDequeue(out EventSource source))
                    {
                        if (source != null)
                        {
                            DisableEvents(source);
                            source.Dispose();
                        }
                    }

                    if (_flushTimer != null)
                    {
                        _flushTimer.Dispose();
                    }
                    base.Dispose();
                    // ensure any remaining logs are flushed
                    Flush();
                }
                _disposed = true;
            }
        }

        public override void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}