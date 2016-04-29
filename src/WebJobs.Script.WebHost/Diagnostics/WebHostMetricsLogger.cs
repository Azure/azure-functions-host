// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Diagnostics;

namespace WebJobs.Script.WebHost.Diagnostics
{
    public class WebHostMetricsLogger : IMetricsLogger
    {
        private MetricsEventManager _metricsEventManager;

        public WebHostMetricsLogger()
            : this(new MetricsEventGenerator(), 5)
        {
        }

        public WebHostMetricsLogger(IMetricsEventGenerator metricsEventGenerator, int metricEventIntervalInSeconds)
        {
            _metricsEventManager = new MetricsEventManager(metricsEventGenerator, metricEventIntervalInSeconds);
        }

        public void BeginEvent(MetricEvent metricEvent)
        {
            FunctionStartedEvent startedEvent = metricEvent as FunctionStartedEvent;
            if (startedEvent != null)
            {
                startedEvent.StartTime = DateTime.Now;
                _metricsEventManager.FunctionStarted(startedEvent);
            }
        }

        public void EndEvent(MetricEvent metricEvent)
        {
            FunctionStartedEvent completedEvent = metricEvent as FunctionStartedEvent;
            if (completedEvent != null)
            {
                completedEvent.EndTime = DateTime.Now;
                _metricsEventManager.FunctionCompleted(completedEvent);
            }
        }

        public void LogEvent(MetricEvent metricEvent)
        {
            HostStarted hostStartedEvent = metricEvent as HostStarted;
            if (hostStartedEvent != null)
            {
                _metricsEventManager.HostStarted(hostStartedEvent.Host);
            }
        }
    }
}