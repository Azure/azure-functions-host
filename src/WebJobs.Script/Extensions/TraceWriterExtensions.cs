// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class TraceWriterExtensions
    {
        public static TraceWriter Conditional(this TraceWriter traceWriter, Func<TraceEvent, bool> predicate)
        {
            return new ConditionalTraceWriter(traceWriter, predicate);
        }

        public static TraceWriter Apply(this TraceWriter traceWriter, IDictionary<string, object> properties)
        {
            if (properties == null)
            {
                throw new ArgumentNullException(nameof(properties));
            }

            Action<TraceEvent> interceptor = (t) =>
            {
                // apply the properties
                foreach (var property in properties)
                {
                    if (!t.Properties.ContainsKey(property.Key))
                    {
                        t.Properties.Add(property.Key, property.Value);
                    }
                }
            };
            return new InterceptingTraceWriter(traceWriter, interceptor);
        }

        public static TraceWriter WithSource(this TraceWriter traceWriter, string source)
            => new InterceptingTraceWriter(traceWriter, t => t.Source = source);


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

        public static void VerboseFormat(this TraceWriter traceWriter, string messageFormat, params object[] parameters)
        {
            string message = string.Format(messageFormat, parameters);
            traceWriter.Verbose(message);
        }

        public static void InfoFormat(this TraceWriter traceWriter, string messageFormat, params object[] parameters)
        {
            string message = string.Format(messageFormat, parameters);
            traceWriter.Info(message);
        }

        public static void WarningFormat(this TraceWriter traceWriter, string messageFormat, params object[] parameters)
        {
            string message = string.Format(messageFormat, parameters);
            traceWriter.Warning(message);
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
