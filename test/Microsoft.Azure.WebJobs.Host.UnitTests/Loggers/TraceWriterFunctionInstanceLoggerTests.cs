// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Loggers
{
    public class TraceWriterFunctionInstanceLoggerTests
    {
        private readonly Mock<TraceWriter> _mockTraceWriter;
        private readonly TraceWriterFunctionInstanceLogger _logger;

        public TraceWriterFunctionInstanceLoggerTests()
        {
            _mockTraceWriter = new Mock<TraceWriter>(MockBehavior.Strict, TraceLevel.Warning);
            _logger = new TraceWriterFunctionInstanceLogger(_mockTraceWriter.Object);
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

            _mockTraceWriter.Setup(p => p.Trace(TraceLevel.Info, Host.TraceSource.Execution, "Executing: 'TestJob' - Reason: 'TestReason'", null));

            await _logger.LogFunctionStartedAsync(message, CancellationToken.None);

            _mockTraceWriter.VerifyAll();
        }

        [Fact]
        public async Task LogFunctionCompletedAsync_CallsTraceWriter()
        {
            FunctionDescriptor descriptor = new FunctionDescriptor
            {
                ShortName = "TestJob"
            };
            FunctionCompletedMessage successMessage = new FunctionCompletedMessage
            {
                Function = descriptor
            };

            Exception ex = new Exception("Kaboom!");
            FunctionCompletedMessage failureMessage = new FunctionCompletedMessage
            {
                Function = descriptor,
                Failure = new FunctionFailure { Exception = ex },
                FunctionInstanceId = new Guid("8d71c9e3-e809-4cfb-bb78-48ae25c7d26d")
            };

            _mockTraceWriter.Setup(p => p.Trace(TraceLevel.Info, Host.TraceSource.Execution, "Executed: 'TestJob' (Succeeded)", null));
            _mockTraceWriter.Setup(p => p.Trace(TraceLevel.Error, Host.TraceSource.Execution, "Executed: 'TestJob' (Failed)", ex));
            _mockTraceWriter.Setup(p => p.Trace(TraceLevel.Error, Host.TraceSource.Host, "  Function had errors. See Azure WebJobs SDK dashboard for details. Instance ID is '8d71c9e3-e809-4cfb-bb78-48ae25c7d26d'", null));

            await _logger.LogFunctionCompletedAsync(successMessage, CancellationToken.None);
            await _logger.LogFunctionCompletedAsync(failureMessage, CancellationToken.None);

            _mockTraceWriter.VerifyAll();
        }
    }
}
