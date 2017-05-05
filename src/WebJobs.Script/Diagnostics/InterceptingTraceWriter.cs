// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host;

namespace Microsoft.Azure.WebJobs.Script
{
    public sealed class InterceptingTraceWriter : TraceWriter, IDisposable
    {
        private readonly Action<TraceEvent> _interceptor;
        private bool _disposed = false;

        public InterceptingTraceWriter(TraceWriter innerWriter, Action<TraceEvent> interceptor)
            : base(innerWriter?.Level ?? TraceLevel.Off)
        {
            InnerWriter = innerWriter ?? throw new ArgumentNullException(nameof(innerWriter));
            _interceptor = interceptor ?? throw new ArgumentNullException(nameof(interceptor));
        }

        public TraceWriter InnerWriter { get; }

        public override void Trace(TraceEvent traceEvent)
        {
            if (Level < traceEvent.Level)
            {
                return;
            }

            _interceptor(traceEvent);
            InnerWriter.Trace(traceEvent);
        }

        public override void Flush()
        {
            InnerWriter.Flush();
            base.Flush();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                (InnerWriter as IDisposable)?.Dispose();
                _disposed = true;
            }
        }
    }
}
