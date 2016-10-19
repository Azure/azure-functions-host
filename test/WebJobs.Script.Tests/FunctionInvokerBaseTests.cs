// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionInvokerBaseTests
    {
        [Fact]
        public void LogInvocationMetrics_EmitsExpectedEvents()
        {
            var metrics = new TestMetricsLogger();
            Collection<BindingMetadata> bindings = new Collection<BindingMetadata>
            {
                new BindingMetadata { Type = "httpTrigger" },
                new BindingMetadata { Type = "blob", Direction = BindingDirection.In },
                new BindingMetadata { Type = "blob", Direction = BindingDirection.Out },
                new BindingMetadata { Type = "table", Direction = BindingDirection.In },
                new BindingMetadata { Type = "table", Direction = BindingDirection.In }
            };

            FunctionInvokerBase.LogInvocationMetrics(metrics, bindings);

            Assert.Equal(6, metrics.LoggedEvents.Count);
            Assert.Equal("function.invoke", metrics.LoggedEvents[0]);
            Assert.Equal("function.binding.httpTrigger", metrics.LoggedEvents[1]);
            Assert.Equal("function.binding.blob.In", metrics.LoggedEvents[2]);
            Assert.Equal("function.binding.blob.Out", metrics.LoggedEvents[3]);
            Assert.Equal("function.binding.table.In", metrics.LoggedEvents[4]);
            Assert.Equal("function.binding.table.In", metrics.LoggedEvents[5]);
        }

        private class TestMetricsLogger : IMetricsLogger
        {
            public TestMetricsLogger()
            {
                LoggedEvents = new Collection<string>();
            }

            public Collection<string> LoggedEvents { get; }

            public void BeginEvent(MetricEvent metricEvent)
            {
                throw new NotImplementedException();
            }

            public object BeginEvent(string eventName)
            {
                throw new NotImplementedException();
            }

            public void EndEvent(object eventHandle)
            {
                throw new NotImplementedException();
            }

            public void EndEvent(MetricEvent metricEvent)
            {
                throw new NotImplementedException();
            }

            public void LogEvent(string eventName)
            {
                LoggedEvents.Add(eventName);
            }

            public void LogEvent(MetricEvent metricEvent)
            {
                throw new NotImplementedException();
            }
        }
    }
}
