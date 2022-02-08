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
            metadata.Bindings.Add(new BindingMetadata { Type = "blob", Direction = BindingDirection.Out });
            metadata.Bindings.Add(new BindingMetadata { Type = "table", Direction = BindingDirection.In });
            var invokeLatencyEvent = _functionInstanceLogger.LogInvocationMetrics(metadata);

            Assert.Equal($"{MetricEventNames.FunctionInvokeLatency}_testfunction", (string)invokeLatencyEvent);

            Assert.Equal(3, _metrics.LoggedEvents.Count());
            Assert.Contains("function.binding.httptrigger_testfunction", _metrics.LoggedEvents);
            Assert.Contains("function.binding.blob_testfunction", _metrics.LoggedEvents);
            Assert.Contains("function.binding.table_testfunction", _metrics.LoggedEvents);

            // log the events once more
            invokeLatencyEvent = _functionInstanceLogger.LogInvocationMetrics(metadata);
            Assert.Equal(6, _metrics.LoggedEvents.Count());
        }
    }
}
