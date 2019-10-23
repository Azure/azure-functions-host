// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    /**
     * This decorator is required to enforce a single instance of WebHostMetricsLogger across webhost and jobhost and also not letting it get disposed during jobhost restarts. The host DI implementation disposes of registered instances, which is a behavior that cannot be changed.
     */
    internal class NonDisposableMetricsLogger : IMetricsLogger
    {
        private readonly IMetricsLogger _metricsLogger;

        public NonDisposableMetricsLogger(IMetricsLogger metricsLogger)
        {
            _metricsLogger = metricsLogger;
        }

        public object BeginEvent(string eventName, string functionName = null, string data = null)
        {
            return _metricsLogger.BeginEvent(eventName, functionName, data);
        }

        public void BeginEvent(MetricEvent metricEvent)
        {
            _metricsLogger.BeginEvent(metricEvent);
        }

        public void EndEvent(MetricEvent metricEvent)
        {
            _metricsLogger.EndEvent(metricEvent);
        }

        public void EndEvent(object eventHandle)
        {
            _metricsLogger.EndEvent(eventHandle);
        }

        public void LogEvent(MetricEvent metricEvent)
        {
            _metricsLogger.LogEvent(metricEvent);
        }

        public void LogEvent(string eventName, string functionName = null, string data = null)
        {
            _metricsLogger.LogEvent(eventName, functionName, data);
        }
    }
}