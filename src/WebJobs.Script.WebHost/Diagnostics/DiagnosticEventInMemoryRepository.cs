// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Timers;
using Microsoft.Extensions.Logging;
using Timer = System.Timers.Timer;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class DiagnosticEventInMemoryRepository : IDiagnosticEventRepository
    {
        private readonly ConcurrentDictionary<string, DiagnosticEvent> _events = new ConcurrentDictionary<string, DiagnosticEvent>();
        private readonly Timer _resetTimer;

        public DiagnosticEventInMemoryRepository()
        {
            _resetTimer = new Timer()
            {
                AutoReset = true,
                Interval = 600000,
                Enabled = true
            };

            _resetTimer.Elapsed += OnFlushLogs;
        }

        private void OnFlushLogs(object sender, ElapsedEventArgs e)
        {
            FlushLogs();
        }

        public void AddDiagnosticEvent(DateTime timestamp, string errorCode, LogLevel level, string message, string helpLink, Exception exception)
        {
            var diagnosticEvent = new DiagnosticEvent()
            {
                ErrorCode = errorCode,
                HelpLink = helpLink,
                LastOccuredTimeStamp = timestamp,
                Message = message,
                Level = level,
                Details = exception?.ToFormattedString()
            };

            _events.AddOrUpdate(errorCode, diagnosticEvent, (e, a) =>
            {
                a.IncrementHitCount();
                a.LastOccuredTimeStamp = timestamp;
                return a;
            });
        }

        public IEnumerable<DiagnosticEvent> GetEvents()
        {
            return _events.Values;
        }

        public void FlushLogs()
        {
            _events.Clear();
        }
    }
}
