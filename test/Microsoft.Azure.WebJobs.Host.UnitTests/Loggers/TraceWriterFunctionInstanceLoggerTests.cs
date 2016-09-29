// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Loggers
{
    public class TraceWriterFunctionInstanceLoggerTests
    {
        private readonly TestTraceWriter _traceWriter;
        private readonly TraceWriterFunctionInstanceLogger _logger;

        public TraceWriterFunctionInstanceLoggerTests()
        {
            _traceWriter = new TestTraceWriter(TraceLevel.Verbose);
            _logger = new TraceWriterFunctionInstanceLogger(_traceWriter);
        }

        [Fact]
        public async Task LogFunctionStartedAsync_CallsTraceWriter()
        {
            FunctionStartedMessage message = new FunctionStartedMessage
            {
                Function = new FunctionDescriptor
                {
                    ShortName = "TestJob"
                },
                ReasonDetails = "TestReason",
                HostInstanceId = Guid.NewGuid(),
                FunctionInstanceId = Guid.NewGuid()
            };

            await _logger.LogFunctionStartedAsync(message, CancellationToken.None);

            Assert.Equal(1, _traceWriter.Traces.Count);
            TraceEvent traceEvent = _traceWriter.Traces[0];
            Assert.Equal(TraceLevel.Info, traceEvent.Level);
            Assert.Equal(Host.TraceSource.Execution, traceEvent.Source);
            Assert.Equal("Executing: 'TestJob' - Reason: 'TestReason'", traceEvent.Message);
            Assert.Equal(3, traceEvent.Properties.Count);
            Assert.Equal(message.HostInstanceId, traceEvent.Properties["MS_HostInstanceId"]);
            Assert.Equal(message.FunctionInstanceId, traceEvent.Properties["MS_FunctionInvocationId"]);
            Assert.Same(message.Function, traceEvent.Properties["MS_FunctionDescriptor"]);
        }

        [Fact]
        public async Task LogFunctionCompletedAsync_CallsTraceWriter()
        {
            FunctionDescriptor descriptor = new FunctionDescriptor
            {
                ShortName = "TestJob",
                FullName = "TestNamespace.TestJob"
            };
            FunctionCompletedMessage successMessage = new FunctionCompletedMessage
            {
                Function = descriptor,
                FunctionInstanceId = Guid.NewGuid(),
                HostInstanceId = Guid.NewGuid()
            };

            Exception ex = new Exception("Kaboom!");
            FunctionCompletedMessage failureMessage = new FunctionCompletedMessage
            {
                Function = descriptor,
                Failure = new FunctionFailure { Exception = ex },
                FunctionInstanceId = new Guid("8d71c9e3-e809-4cfb-bb78-48ae25c7d26d"),
                HostInstanceId = Guid.NewGuid()
            };

            await _logger.LogFunctionCompletedAsync(successMessage, CancellationToken.None);
            await _logger.LogFunctionCompletedAsync(failureMessage, CancellationToken.None);

            Assert.Equal(3, _traceWriter.Traces.Count);

            TraceEvent traceEvent = _traceWriter.Traces[0];
            Assert.Equal(TraceLevel.Info, traceEvent.Level);
            Assert.Equal(Host.TraceSource.Execution, traceEvent.Source);
            Assert.Equal("Executed: 'TestJob' (Succeeded)", traceEvent.Message);
            Assert.Equal(successMessage.HostInstanceId, traceEvent.Properties["MS_HostInstanceId"]);
            Assert.Equal(successMessage.FunctionInstanceId, traceEvent.Properties["MS_FunctionInvocationId"]);
            Assert.Same(successMessage.Function, traceEvent.Properties["MS_FunctionDescriptor"]);

            traceEvent = _traceWriter.Traces[1];
            Assert.Equal(TraceLevel.Error, traceEvent.Level);
            Assert.Equal(Host.TraceSource.Execution, traceEvent.Source);
            Assert.Equal("Executed: 'TestJob' (Failed)", traceEvent.Message);
            Assert.Same(ex, traceEvent.Exception);
            Assert.Equal(failureMessage.HostInstanceId, traceEvent.Properties["MS_HostInstanceId"]);
            Assert.Equal(failureMessage.FunctionInstanceId, traceEvent.Properties["MS_FunctionInvocationId"]);
            Assert.Same(failureMessage.Function, traceEvent.Properties["MS_FunctionDescriptor"]);

            traceEvent = _traceWriter.Traces[2];
            Assert.Equal(TraceLevel.Error, traceEvent.Level);
            Assert.Equal(Host.TraceSource.Host, traceEvent.Source);
            Assert.Equal("  Function had errors. See Azure WebJobs SDK dashboard for details. Instance ID is '8d71c9e3-e809-4cfb-bb78-48ae25c7d26d'", traceEvent.Message);
            Assert.Same(ex, traceEvent.Exception);
            Assert.Equal(failureMessage.HostInstanceId, traceEvent.Properties["MS_HostInstanceId"]);
            Assert.Equal(failureMessage.FunctionInstanceId, traceEvent.Properties["MS_FunctionInvocationId"]);
            Assert.Same(failureMessage.Function, traceEvent.Properties["MS_FunctionDescriptor"]);
        }
    }
}
