// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class FunctionInstanceLoggerTests
    {
        private readonly FunctionInstanceLogger _functionInstanceLogger;
        private readonly TestMetricsLogger _metrics;

        public FunctionInstanceLoggerTests()
        {
            var metadataManager = new Mock<IFunctionMetadataManager>(MockBehavior.Strict);
            _metrics = new TestMetricsLogger();
            _functionInstanceLogger = new FunctionInstanceLogger(metadataManager.Object, _metrics);
        }

        [Fact]
        public void LogInvocationMetrics_EmitsExpectedEvents()
        {
            var metadata = new FunctionMetadata
            {
                Name = "TestFunction"
            };
            metadata.Bindings.Add(new BindingMetadata { Type = "httpTrigger" });
            metadata.Bindings.Add(new BindingMetadata { Type = "blob", Direction = BindingDirection.In });
            metadata.Bindings.Add(new BindingMetadata { Type = "blob", Direction = BindingDirection.Out });
            metadata.Bindings.Add(new BindingMetadata { Type = "table", Direction = BindingDirection.In });
            metadata.Bindings.Add(new BindingMetadata { Type = "table", Direction = BindingDirection.In });
            var invokeLatencyEvent = _functionInstanceLogger.LogInvocationMetrics(metadata);

            Assert.Equal($"{MetricEventNames.FunctionInvokeLatency}_testfunction", (string)invokeLatencyEvent);

            Assert.Equal(5, _metrics.LoggedEvents.Count());
            Assert.Contains("function.binding.httptrigger_testfunction", _metrics.LoggedEvents);
            Assert.Equal(1, _metrics.LoggedEvents.Count(x => x == "function.binding.httptrigger_testfunction"));

            // for non-trigger bindings, event name does not include direction.
            // So log entries for input and output binding will have same event name.
            Assert.Equal(2, _metrics.LoggedEvents.Count(x => x == "function.binding.blob_testfunction"));
            Assert.Equal(2, _metrics.LoggedEvents.Count(x => x == "function.binding.table_testfunction"));

            // log the events once more
            invokeLatencyEvent = _functionInstanceLogger.LogInvocationMetrics(metadata);
            Assert.Equal(10, _metrics.LoggedEvents.Count());
        }
    }
}
