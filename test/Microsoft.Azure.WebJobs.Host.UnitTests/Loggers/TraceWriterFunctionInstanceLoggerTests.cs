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
                ReasonDetails = "TestReason"
            };

            await _logger.LogFunctionStartedAsync(message, CancellationToken.None);

            Assert.Equal(1, _traceWriter.Traces.Count);
            TraceEvent traceEvent = _traceWriter.Traces[0];
            Assert.Equal(TraceLevel.Info, traceEvent.Level);
            Assert.Equal(Host.TraceSource.Execution, traceEvent.Source);
            Assert.Equal("Executing: 'TestJob' - Reason: 'TestReason'", traceEvent.Message);
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
                FunctionInstanceId = Guid.NewGuid()
            };

            Exception ex = new Exception("Kaboom!");
            FunctionCompletedMessage failureMessage = new FunctionCompletedMessage
            {
                Function = descriptor,
                Failure = new FunctionFailure { Exception = ex },
                FunctionInstanceId = new Guid("8d71c9e3-e809-4cfb-bb78-48ae25c7d26d")
            };

            await _logger.LogFunctionCompletedAsync(successMessage, CancellationToken.None);
            await _logger.LogFunctionCompletedAsync(failureMessage, CancellationToken.None);

            Assert.Equal(3, _traceWriter.Traces.Count);

            TraceEvent traceEvent = _traceWriter.Traces[0];
            Assert.Equal(TraceLevel.Info, traceEvent.Level);
            Assert.Equal(Host.TraceSource.Execution, traceEvent.Source);
            Assert.Equal("Executed: 'TestJob' (Succeeded)", traceEvent.Message);

            traceEvent = _traceWriter.Traces[1];
            Assert.Equal(TraceLevel.Error, traceEvent.Level);
            Assert.Equal(Host.TraceSource.Execution, traceEvent.Source);
            Assert.Equal("Executed: 'TestJob' (Failed)", traceEvent.Message);
            Assert.Same(ex, traceEvent.Exception);

            traceEvent = _traceWriter.Traces[2];
            Assert.Equal(TraceLevel.Error, traceEvent.Level);
            Assert.Equal(Host.TraceSource.Host, traceEvent.Source);
            Assert.Equal("  Function had errors. See Azure WebJobs SDK dashboard for details. Instance ID is '8d71c9e3-e809-4cfb-bb78-48ae25c7d26d'", traceEvent.Message);
            Assert.Same(ex, traceEvent.Exception);
        }
    }
}
