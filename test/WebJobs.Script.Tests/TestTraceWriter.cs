// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host;

namespace WebJobs.Script.Tests
{
    public class TestTraceWriter : TraceWriter
    {
        private Collection<TraceEvent> _traces = new Collection<TraceEvent>();

        public TestTraceWriter(TraceLevel level) : base(level)
        {
        }

        public Collection<TraceEvent> Traces
        {
            get
            {
                return _traces;
            }
        }

        public bool Flushed { get; private set; }

        public override void Trace(TraceEvent traceEvent)
        {
            Traces.Add(traceEvent);
        }

        public override void Flush()
        {
            Flushed = true;

            base.Flush();
        }
    }
}
