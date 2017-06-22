// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#if FASTLOGGER //will we continue to support the FastLogger implementation?
using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class FastLoggerTests
    {
        [Fact]
        public void OnException_TracesException()
        {
            var trace = new TestTraceWriter(TraceLevel.Verbose);
            var ex = new InvalidOperationException("Boom!");
            FunctionInstanceLogger.OnException(ex, trace);

            TraceEvent traceEvent = trace.Traces.Single();
            Assert.StartsWith("Error writing logs to table storage: System.InvalidOperationException: Boom!", traceEvent.Message);
            Assert.Equal(TraceLevel.Error, traceEvent.Level);
            Assert.Same(ex, traceEvent.Exception);
        }
    }
}
#endif