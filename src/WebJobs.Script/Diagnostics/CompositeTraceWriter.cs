// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host;

namespace Microsoft.Azure.WebJobs.Script
{
    // TODO: The core WebJobs SDK also defines a CompositeTraceWriter, but that is internal.
    // We should consider exposing the core SDK CompositeTraceWriter and adopting that instead.
    public class CompositeTraceWriter : TraceWriter, IDisposable
    {
        private readonly IEnumerable<TraceWriter> _innerTraceWriters;
        private bool _disposed = false;

        public CompositeTraceWriter(IEnumerable<TraceWriter> traceWriters, TraceLevel level = TraceLevel.Verbose)
            : base(level)
        {
            _innerTraceWriters = traceWriters ?? throw new ArgumentNullException("traceWriters");
        }

        public override void Trace(TraceEvent traceEvent)
        {
            if (traceEvent == null)
            {
                throw new ArgumentNullException("traceEvent");
            }

            foreach (var traceWriter in _innerTraceWriters)
            {
                if (traceWriter.Level >= traceEvent.Level)
                {
                    traceWriter.Trace(traceEvent);
                }
            }
        }

        public override void Flush()
        {
            foreach (var traceWriter in _innerTraceWriters)
            {
                traceWriter.Flush();
            }

            base.Flush();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    foreach (var traceWriter in _innerTraceWriters)
                    {
                        (traceWriter as IDisposable)?.Dispose();
                    }
                }

                _disposed = true;
            }
        }
    }
}
