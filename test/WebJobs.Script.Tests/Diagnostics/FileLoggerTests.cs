// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Internal;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class FileLoggerTests
    {
        [Fact]
        public void FileLogger_NoFunctionName()
        {
            var trace = new TestTraceWriter(TraceLevel.Info);

            // we should never call this
            var factoryMock = new Mock<IFunctionTraceWriterFactory>(MockBehavior.Strict);

            var logger = new FileLogger("SomeCategory", factoryMock.Object, (c, l) => true);

            // FunctionName comes from scope -- call with no scope values
            logger.Log(LogLevel.Information, 0, new FormattedLogValues("Some Message"), null, (s, e) => s.ToString());

            Assert.Empty(trace.Traces);
            factoryMock.Verify(f => f.Create(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void FileLogger_FunctionName_FromScope()
        {
            var trace = new TestTraceWriter(TraceLevel.Info);

            var factoryMock = new Mock<IFunctionTraceWriterFactory>(MockBehavior.Strict);
            factoryMock
                .Setup(f => f.Create("SomeFunction"))
                .Returns(trace);

            var logger = new FileLogger("SomeCategory", factoryMock.Object, (c, l) => true);

            // FunctionName comes from scope
            using (logger.BeginScope(new Dictionary<string, object>
            {
                [ScriptConstants.LoggerFunctionNameKey] = "SomeFunction"
            }))
            {
                logger.Log(LogLevel.Information, 0, new FormattedLogValues("Some Message"), null, (s, e) => s.ToString());
            }

            var traceEvent = trace.Traces.Single();
            Assert.Equal(TraceLevel.Info, traceEvent.Level);
            Assert.Equal("Some Message", traceEvent.Message);
            Assert.Equal("SomeCategory", traceEvent.Source);
        }
    }
}
