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
    internal sealed class CompositeTraceWriter : TraceWriter
    {
        private readonly IEnumerable<TraceWriter> _innerTraceWriters;

        public CompositeTraceWriter(IEnumerable<TraceWriter> traceWriters, TraceLevel level = TraceLevel.Verbose)
            : base(level)
        {
            if (traceWriters == null)
            {
                throw new ArgumentNullException("traceWriters");
            }

            _innerTraceWriters = traceWriters;
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
    }
}
