// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// A TraceWriter used explicitly to log the MetricEventNames.FunctionUserLog event. This allows us to count the number
    /// of user logs that the SystemTraceWriter ignores.
    /// </summary>
    internal class UserLogMetricsTraceWriter : TraceWriter
    {
        private readonly IMetricsLogger _metricsLogger;
        private readonly string _functionName;

        public UserLogMetricsTraceWriter(IMetricsLogger metricsLogger, string functionName, TraceLevel level)
            : base(level)
        {
            _metricsLogger = metricsLogger ?? new MetricsLogger();
            _functionName = functionName;
        }

        public override void Trace(TraceEvent traceEvent)
        {
            if (Level < traceEvent.Level || traceEvent.Properties?.Count == 0)
            {
                return;
            }

            if (traceEvent.Properties.TryGetValue(ScriptConstants.TracePropertyIsUserTraceKey, out object value)
                && value is bool && (bool)value == true)
            {
                // we only want to track user logs
                _metricsLogger.LogEvent(MetricEventNames.FunctionUserLog, _functionName);
            }
        }
    }
}
