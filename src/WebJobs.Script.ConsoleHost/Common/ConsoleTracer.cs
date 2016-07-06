// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host;

namespace WebJobs.Script.ConsoleHost.Common
{
    public class ConsoleTracer : TraceWriter
    {
        public ConsoleTracer(TraceLevel level) : base(level)
        { }

        public override void Trace(TraceEvent traceEvent)
        {
            if (traceEvent.Source == Constants.CliTracingSource)
                Console.WriteLine(traceEvent.Message);
        }
    }
}
