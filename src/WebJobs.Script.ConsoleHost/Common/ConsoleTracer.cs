// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host;
using System;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.ObjectModel;

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
