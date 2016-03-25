﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Diagnostics;

namespace WebJobs.Script.WebHost.Diagnostics
{
    public class WebHostMetricsLogger : IMetricsLogger
    {
        public void BeginEvent(MetricEvent metricEvent)
        {
            FunctionStartedEvent startedEvent = metricEvent as FunctionStartedEvent;
            if (startedEvent != null)
            {
                startedEvent.StartTime = DateTime.Now;
                MetricsEventManager.FunctionStarted(startedEvent);
            }
        }

        public void EndEvent(MetricEvent metricEvent)
        {
            FunctionStartedEvent completedEvent = metricEvent as FunctionStartedEvent;
            if (completedEvent != null)
            {
                completedEvent.EndTime = DateTime.Now;
                MetricsEventManager.FunctionCompleted(completedEvent);
            }
        }

        public void LogEvent(MetricEvent metricEvent)
        {
            HostStarted hostStartedEvent = metricEvent as HostStarted;
            if (hostStartedEvent != null)
            {
                MetricsEventManager.HostStarted(hostStartedEvent.Host);
            }
        }
    }
}