// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Script.Eventing
{
    public sealed class StructuredLogEntryEvent : ScriptEvent
    {
        private readonly Lazy<StructuredLogEntry> _logEntry;

        public StructuredLogEntryEvent(StructuredLogEntry logEntry)
            : this(logEntry, string.Empty)
        {
        }

        public StructuredLogEntryEvent(StructuredLogEntry logEntry, string source)
            : this(() => logEntry, source)
        {
        }

        public StructuredLogEntryEvent(Func<StructuredLogEntry> logEntryFactory)
            : this(logEntryFactory, string.Empty)
        {
        }

        public StructuredLogEntryEvent(Func<StructuredLogEntry> logEntryFactory, string source)
            : base(nameof(StructuredLogEntryEvent), source)
        {
            _logEntry = new Lazy<StructuredLogEntry>(logEntryFactory, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public StructuredLogEntry LogEntry => _logEntry.Value;
    }
}
