// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
using System.Collections.ObjectModel;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestMetricsLogger : IMetricsLogger
    {
        public TestMetricsLogger()
        {
            LoggedEvents = new Collection<string>();
            LoggedMetricEvents = new Collection<MetricEvent>();
            MetricEventsBegan = new Collection<MetricEvent>();
            EventsBegan = new Collection<string>();
            MetricEventsEnded = new Collection<MetricEvent>();
            EventsEnded = new Collection<object>();
        }

        public Collection<string> LoggedEvents { get; }

        public Collection<MetricEvent> LoggedMetricEvents { get; }

        public Collection<MetricEvent> MetricEventsBegan { get; }

        public Collection<MetricEvent> MetricEventsEnded { get; }

        public Collection<string> EventsBegan { get; }

        public Collection<object> EventsEnded { get; }

        public void BeginEvent(MetricEvent metricEvent)
        {
            MetricEventsBegan.Add(metricEvent);
        }

        public object BeginEvent(string eventName, string functionName = null)
        {
            string key = MetricsEventManager.GetAggregateKey(eventName, functionName);
            EventsBegan.Add(key);
            return key;
        }

        public void EndEvent(object eventHandle)
        {
            EventsEnded.Add(eventHandle);
        }

        public void EndEvent(MetricEvent metricEvent)
        {
            MetricEventsEnded.Add(metricEvent);
        }

        public void LogEvent(string eventName, string functionName = null)
        {
            LoggedEvents.Add(MetricsEventManager.GetAggregateKey(eventName, functionName));
        }

        public void LogEvent(MetricEvent metricEvent)
        {
            LoggedMetricEvents.Add(metricEvent);
        }
    }
}