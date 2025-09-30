// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.Metrics;
using System.Diagnostics.Tracing;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    public sealed partial class ApplicationInsightsMetricExporter
    {
        [EventSource(Name = "Azure-Functions-Application-Insights-Metric-Exporter")]
        private sealed class Events : EventSource
        {
            public static readonly Events Log = new();

            private Events()
            {
            }

            [Event(1, Message ="Failed to collect observable instruments: {0}", Level = EventLevel.Error)]
            public void FailedToCollectInstruments(Exception error)
            {
                WriteEvent(1, $"{error.GetType().FullName}: {error.Message}");
            }

            [Event(2, Message = "Begin collecting observable instruments.")]
            public void BeginCollectObservables()
            {
                WriteEvent(2);
            }

            [Event(3, Message = "End collecting observable instruments.")]
            public void EndCollectObservables()
            {
                WriteEvent(2);
            }

            [Event(4, Message = "Meter listening started.")]
            public void MeterListeningStarted()
            {
                WriteEvent(4);
            }

            [Event(5, Message = "Meter listening stopped.")]
            public void MeterListeningStopped()
            {
                WriteEvent(5);
            }

            [Event(6, Message = "Subscribed to instrument {0} on meter {1}.")]
            public void SubscribedToInstrument(Instrument instrument)
            {
                WriteEvent(6, instrument.Name, instrument.Meter.Name);
            }
        }
    }
}
