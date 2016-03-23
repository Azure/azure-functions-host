// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

using Microsoft.Azure.WebJobs.Script;
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
                MetricsEventManager.FunctionStarted();
            }
        }

        public void EndEvent(MetricEvent metricEvent)
        {
            FunctionStartedEvent startedEvent = metricEvent as FunctionStartedEvent;
            if (startedEvent != null)
            {
                startedEvent.EndTime = DateTime.Now;
                MetricsEventManager.FunctionCompleted();
            }
        }

        public void HostStartedEvent(MetricEvent metricEvent)
        {
            ScriptHostStartedEvent scriptHostStartedEvent = metricEvent as ScriptHostStartedEvent;
            if (scriptHostStartedEvent != null)
            {
                MetricsEventManager.HostStartedEvent(scriptHostStartedEvent.ScriptHost);
            }
        }
    }
}