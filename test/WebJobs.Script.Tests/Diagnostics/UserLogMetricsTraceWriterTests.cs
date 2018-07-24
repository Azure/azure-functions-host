// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using System.Linq;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class UserLogMetricsTraceWriterTests
    {
        private const string FunctionName = "FunctionName";

        [Fact]
        public void Trace_WritesMetric_ForUserLogs()
        {
            var metrics = new TestMetricsLogger();
            var traceWriter = new UserLogMetricsTraceWriter(metrics, FunctionName, TraceLevel.Info);

            var traceEvent = new TraceEvent(TraceLevel.Info, string.Empty);
            traceEvent.Properties[ScriptConstants.TracePropertyIsUserTraceKey] = true;
            traceWriter.Trace(traceEvent);

            var expectedEvent = MetricsEventManager.GetAggregateKey(MetricEventNames.FunctionUserLog, FunctionName);
            string eventName = metrics.LoggedEvents.Single();
            Assert.Equal(expectedEvent, eventName);
        }

        [Fact]
        public void Trace_FiltersOnLevel()
        {
            var metrics = new TestMetricsLogger();
            var traceWriter = new UserLogMetricsTraceWriter(metrics, FunctionName, TraceLevel.Error);

            var traceEvent = new TraceEvent(TraceLevel.Info, string.Empty);
            traceEvent.Properties[ScriptConstants.TracePropertyIsUserTraceKey] = true;
            traceWriter.Trace(traceEvent);

            Assert.Empty(metrics.LoggedEvents);
        }

        [Fact]
        public void Trace_IgnoresHostLogs()
        {
            var metrics = new TestMetricsLogger();
            var traceWriter = new UserLogMetricsTraceWriter(metrics, FunctionName, TraceLevel.Info);

            traceWriter.Info(string.Empty);

            Assert.Empty(metrics.LoggedEvents);
        }
    }
}
