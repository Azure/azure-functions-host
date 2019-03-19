// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
using System.Collections.Concurrent;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestMetricsLogger : IMetricsLogger
    {
        public TestMetricsLogger()
        {
            LoggedEvents = new ConcurrentBag<string>();
            LoggedMetricEvents = new ConcurrentBag<MetricEvent>();
            MetricEventsBegan = new ConcurrentBag<MetricEvent>();
            EventsBegan = new ConcurrentBag<string>();
            MetricEventsEnded = new ConcurrentBag<MetricEvent>();
            EventsEnded = new ConcurrentBag<object>();
        }

        public ConcurrentBag<string> LoggedEvents { get; }

        public ConcurrentBag<MetricEvent> LoggedMetricEvents { get; }

        public ConcurrentBag<MetricEvent> MetricEventsBegan { get; }

        public ConcurrentBag<MetricEvent> MetricEventsEnded { get; }

        public ConcurrentBag<string> EventsBegan { get; }

        public ConcurrentBag<object> EventsEnded { get; }

        public void ClearCollections()
        {
            LoggedEvents.Clear();
            LoggedMetricEvents.Clear();
            MetricEventsBegan.Clear();
            EventsBegan.Clear();
            MetricEventsEnded.Clear();
            EventsEnded.Clear();
        }

        public void BeginEvent(MetricEvent metricEvent)
        {
            MetricEventsBegan.Add(metricEvent);
        }

        public object BeginEvent(string eventName, string functionName = null, string data = null)
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

        public void LogEvent(string eventName, string functionName = null, string data = null)
        {
            LoggedEvents.Add(MetricsEventManager.GetAggregateKey(eventName, functionName));
        }

        public void LogEvent(MetricEvent metricEvent)
        {
            LoggedMetricEvents.Add(metricEvent);
        }
    }
}