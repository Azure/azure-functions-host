// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class DisposableTraceWriter : TraceWriter, IDisposable
    {
        public DisposableTraceWriter()
            : base(TraceLevel.Verbose)
        {
        }

        public bool IsDisposed { get; private set; } = false;

        public void Dispose()
        {
            IsDisposed = true;
        }

        public override void Trace(TraceEvent traceEvent)
        {
            // no-op
        }
    }
}
