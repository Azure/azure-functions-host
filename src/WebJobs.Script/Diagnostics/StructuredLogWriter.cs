// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Eventing;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    public sealed class StructuredLogWriter : IDisposable
    {
        private readonly IDisposable _subscription;
        private readonly FileTraceWriter _traceWriter;
        private bool _disposedValue = false;

        public StructuredLogWriter(IScriptEventManager eventManager, string baseLogPath)
        {
            string logPath = Path.Combine(baseLogPath, "structured");
            _traceWriter = new FileTraceWriter(logPath, TraceLevel.Verbose, s => s);

            _subscription = eventManager.OfType<StructuredLogEntryEvent>()
                .Subscribe(OnLogEntry);
        }

        private void OnLogEntry(StructuredLogEntryEvent logEvent)
        {
            string message = logEvent.LogEntry.ToJsonLineString();
            _traceWriter.Trace(message, TraceLevel.Verbose, null);
        }

        private void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _subscription?.Dispose();
                    _traceWriter?.Dispose();
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
