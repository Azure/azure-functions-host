// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Diagnostics;

namespace WebJobs.Script.WebHost.Diagnostics
{
    public class WebHostMetricsLogger : IMetricsLogger
    {
        public void BeginEvent(MetricEvent metricEvent)
        {
            // TODO
            FunctionStartedEvent startedEvent = metricEvent as FunctionStartedEvent;
            if (startedEvent != null)
            {
                startedEvent.StartTime = DateTime.Now;
            }
        }

        public void EndEvent(MetricEvent metricEvent)
        {
            // TODO
            FunctionStartedEvent startedEvent = metricEvent as FunctionStartedEvent;
            if (startedEvent != null)
            {
                startedEvent.EndTime = DateTime.Now;
            }
        }
    }
}