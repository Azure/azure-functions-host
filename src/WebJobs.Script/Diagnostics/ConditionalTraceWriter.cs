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
        private readonly FailedConditionTraceBehavior _failedConditionBehavior;

        public ConditionalTraceWriter(TraceWriter innerWriter, Func<TraceEvent, bool> predicate, FailedConditionTraceBehavior failedConditionBehavior = FailedConditionTraceBehavior.TraceAsVerbose) 
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
            _failedConditionBehavior = failedConditionBehavior;
        }

        public TraceWriter InnerWriter { get; }

        public override void Trace(TraceEvent traceEvent)
        {
            if (_predicate(traceEvent))
            {
                InnerWriter.Trace(traceEvent);
            }
            else if (_failedConditionBehavior == FailedConditionTraceBehavior.TraceAsVerbose)
            {
                var eventClone = CloneEvent(traceEvent);

                // To improve troubleshooting, instead of completely suppressing the event, we'll downgrade
                // it to 'Verbose' and add a prefix to the message.
                eventClone.Message = $"[Supressed '{traceEvent.Level}' trace] {traceEvent.Message}";
                eventClone.Level = TraceLevel.Verbose;

                InnerWriter.Trace(eventClone);
            }
        }

        private static TraceEvent CloneEvent(TraceEvent traceEvent)
        {
            var result = new TraceEvent(traceEvent.Level, traceEvent.Message, 
                traceEvent.Source, traceEvent.Exception)
            {
                Timestamp = traceEvent.Timestamp
            };

            foreach (var property in traceEvent.Properties)
            {
                result.Properties.Add(property);
            }

            return result;
        }
    }
}
