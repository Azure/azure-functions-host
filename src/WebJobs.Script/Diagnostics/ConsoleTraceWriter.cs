// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class ConsoleTraceWriter : TraceWriter
    {
        public ConsoleTraceWriter(TraceLevel traceLevel) : base(traceLevel)
        {
        }

        public override void Trace(TraceEvent traceEvent)
        {
            Console.WriteLine(traceEvent.Message);
        }
    }
}
