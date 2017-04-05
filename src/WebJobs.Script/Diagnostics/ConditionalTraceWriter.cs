// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host;

namespace Microsoft.Azure.WebJobs.Script
{
    public sealed class ConditionalTraceWriter : TraceWriter
    {
        private readonly Func<TraceEvent, bool> _predicate;

        public ConditionalTraceWriter(TraceWriter innerWriter, Func<TraceEvent, bool> predicate)
            : base(innerWriter?.Level ?? TraceLevel.Off)
        {
            if (innerWriter == null)
            {
                throw new ArgumentNullException(nameof(innerWriter));
            }

            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            InnerWriter = innerWriter;
            _predicate = predicate;
        }

        public TraceWriter InnerWriter { get; }

        public override void Trace(TraceEvent traceEvent)
        {
            if (Level < traceEvent.Level)
            {
                return;
            }

            if (_predicate(traceEvent))
            {
                InnerWriter.Trace(traceEvent);
            }
        }

        public override void Flush()
        {
            InnerWriter.Flush();
            base.Flush();
        }
    }
}
