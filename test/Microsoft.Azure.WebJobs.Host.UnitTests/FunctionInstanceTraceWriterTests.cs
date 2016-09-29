// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    public class FunctionInstanceTraceWriterTests
    {
        [Fact]
        public void Works()
        {
            var descriptor = new FunctionDescriptor();
            var instance = new FunctionInstance(Guid.NewGuid(), null, ExecutionReason.AutomaticTrigger, null, null, descriptor);
            var writer = new TestTraceWriter(TraceLevel.Info);
            var functionTraceLevel = TraceLevel.Info;
            var hostInstanceId = Guid.NewGuid();

            var instanceWriter = new FunctionInstanceTraceWriter(instance, hostInstanceId, writer, functionTraceLevel);

            instanceWriter.Info("Test Info");

            var traceEvent = writer.Traces.Single();
            Assert.Equal(3, traceEvent.Properties.Count);
            Assert.Equal(instance.Id, traceEvent.Properties["MS_FunctionInvocationId"]);
            Assert.Equal(hostInstanceId, traceEvent.Properties["MS_HostInstanceId"]);
            Assert.Equal(descriptor, traceEvent.Properties["MS_FunctionDescriptor"]);
        }
    }
}
