// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    /// <summary>
    /// Default implementation of <see cref="IMetricsLogger"/> that doesn't do any logging.
    /// </summary>
    public class MetricsLogger : IMetricsLogger
    {
        public object BeginEvent(string eventName, string functionName = null, string data = null)
        {
            return null;
        }

        public void BeginEvent(MetricEvent metricEvent)
        {
        }

        public void EndEvent(MetricEvent metricEvent)
        {
        }

        public void EndEvent(object eventHandle)
        {
        }

        public void LogEvent(MetricEvent metricEvent)
        {
        }

        public void LogEvent(string eventName, string functionName = null, string data = null)
        {
        }
    }
}
