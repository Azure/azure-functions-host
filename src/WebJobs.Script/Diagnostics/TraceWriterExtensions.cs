// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host;

namespace Microsoft.Azure.WebJobs.Host
{
    public static class TraceWriterExtensions
    {
        public static void Verbose(this TraceWriter traceWriter, string message, IDictionary<string, object> properties)
        {
            TraceEvent traceEvent = CreateEvent(message, TraceLevel.Verbose, properties);
            traceWriter.Trace(traceEvent);
        }

        public static void Info(this TraceWriter traceWriter, string message, IDictionary<string, object> properties)
        {
            TraceEvent traceEvent = CreateEvent(message, TraceLevel.Info, properties);
            traceWriter.Trace(traceEvent);
        }

        public static void Warning(this TraceWriter traceWriter, string message, IDictionary<string, object> properties)
        {
            TraceEvent traceEvent = CreateEvent(message, TraceLevel.Warning, properties);
            traceWriter.Trace(traceEvent);
        }

        public static void Error(this TraceWriter traceWriter, string message, IDictionary<string, object> properties)
        {
            TraceEvent traceEvent = CreateEvent(message, TraceLevel.Error, properties);
            traceWriter.Trace(traceEvent);
        }

        public static void Trace(this TraceWriter traceWriter, string message, TraceLevel level, IDictionary<string, object> properties)
        {
            TraceEvent traceEvent = CreateEvent(message, level, properties);
            traceWriter.Trace(traceEvent);
        }

        private static TraceEvent CreateEvent(string message, TraceLevel level, IDictionary<string, object> properties)
        {
            var traceEvent = new TraceEvent(level, message);

            if (properties != null)
            {
                foreach (var property in properties)
                {
                    traceEvent.Properties.Add(property);
                }
            }

            return traceEvent;
        }
    }
}
