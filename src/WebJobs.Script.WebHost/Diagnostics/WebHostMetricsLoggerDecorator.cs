// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class WebHostMetricsLoggerDecorator : IMetricsLogger
    {
        private readonly IMetricsLogger _webHostMetricsLogger;

        public WebHostMetricsLoggerDecorator(IMetricsLogger webHostMetricsLogger)
        {
            _webHostMetricsLogger = webHostMetricsLogger;
        }

        public object BeginEvent(string eventName, string functionName = null, string data = null)
        {
            return _webHostMetricsLogger.BeginEvent(eventName, functionName, data);
        }

        public void BeginEvent(MetricEvent metricEvent)
        {
            _webHostMetricsLogger.BeginEvent(metricEvent);
        }

        public void EndEvent(MetricEvent metricEvent)
        {
            _webHostMetricsLogger.EndEvent(metricEvent);
        }

        public void EndEvent(object eventHandle)
        {
            _webHostMetricsLogger.EndEvent(eventHandle);
        }

        public void LogEvent(MetricEvent metricEvent)
        {
            _webHostMetricsLogger.LogEvent(metricEvent);
        }

        public void LogEvent(string eventName, string functionName = null, string data = null)
        {
            _webHostMetricsLogger.LogEvent(eventName, functionName, data);
        }
    }
}