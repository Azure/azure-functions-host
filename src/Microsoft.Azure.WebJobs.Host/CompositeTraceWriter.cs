// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Host.Executors;

namespace Microsoft.Azure.WebJobs.Host
{
    /// <summary>
    /// This <see cref="TraceWriter"/> delegates to an inner <see cref="TraceWriter"/> and <see cref="TextWriter"/>.
    /// </summary>
    internal class CompositeTraceWriter : TraceWriter
    {
        private readonly IEnumerable<TraceWriter> _innerTraceWriters;
        private readonly TextWriter _innerTextWriter;

        public CompositeTraceWriter(IEnumerable<TraceWriter> traceWriters, TextWriter textWriter)
            : base(TraceLevel.Verbose)
        {
            _innerTraceWriters = traceWriters ?? Enumerable.Empty<TraceWriter>();
            _innerTextWriter = textWriter;
        }

        public CompositeTraceWriter(TraceWriter traceWriter, TextWriter textWriter)
            : base(TraceLevel.Verbose)
        {
            _innerTraceWriters = traceWriter != null ? new TraceWriter[] { traceWriter } : Enumerable.Empty<TraceWriter>();
            _innerTextWriter = textWriter;
        }

        public override void Trace(TraceEvent traceEvent)
        {
            InvokeTraceWriters(traceEvent);
            InvokeTextWriter(traceEvent);
        }

        protected virtual void InvokeTraceWriters(TraceEvent traceEvent)
        {
            foreach (TraceWriter traceWriter in _innerTraceWriters)
            {
                // filter based on level before delegating
                if (traceWriter.Level >= traceEvent.Level)
                {
                    traceWriter.Trace(traceEvent);
                }
            }
        }

        protected virtual void InvokeTextWriter(TraceEvent traceEvent)
        {
            if (_innerTextWriter != null)
            {
                string message = traceEvent.Message;
                if (!string.IsNullOrEmpty(message) &&
                     message.EndsWith("\r\n", StringComparison.OrdinalIgnoreCase))
                {
                    // remove any terminating return+line feed, since we're
                    // calling WriteLine below
                    message = message.Substring(0, message.Length - 2);
                }

                _innerTextWriter.WriteLine(message);
                if (traceEvent.Exception != null)
                {
                    _innerTextWriter.WriteLine(traceEvent.Exception.ToDetails());
                }
            }
        }

        public override void Flush()
        {
            foreach (TraceWriter traceWriter in _innerTraceWriters)
            {
                traceWriter.Flush();
            }

            if (_innerTextWriter != null)
            {
                _innerTextWriter.Flush();
            }
        }
    }
}
