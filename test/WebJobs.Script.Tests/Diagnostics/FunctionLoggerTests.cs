// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Internal;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class FunctionLoggerTests
    {
        [Fact]
        public void FunctionLogger_NoFunctionName()
        {
            var trace = new TestTraceWriter(TraceLevel.Info);

            // we should never call this
            var factoryMock = new Mock<IFunctionTraceWriterFactory>(MockBehavior.Strict);

            var logger = new FunctionLogger(LogCategories.Function, factoryMock.Object, (c, l) => true);

            // FunctionName comes from scope -- call with no scope values
            logger.Log(LogLevel.Information, 0, new FormattedLogValues("Some Message"), null, (s, e) => s.ToString());

            Assert.Empty(trace.GetTraces());
            factoryMock.Verify(f => f.Create(It.IsAny<string>(), null), Times.Never);
        }

        [Fact]
        public void FunctionLogger_FunctionName_FromScope()
        {
            var trace = new TestTraceWriter(TraceLevel.Info);

            var factoryMock = new Mock<IFunctionTraceWriterFactory>(MockBehavior.Strict);
            factoryMock
                .Setup(f => f.Create("SomeFunction", null))
                .Returns(trace);

            var logger = new FunctionLogger(LogCategories.Function, factoryMock.Object, (c, l) => true);

            // FunctionName comes from scope
            using (logger.BeginScope(new Dictionary<string, object>
            {
                [ScopeKeys.FunctionName] = "SomeFunction"
            }))
            {
                logger.Log(LogLevel.Information, 0, new FormattedLogValues("Some Message"), null, (s, e) => s.ToString());
            }

            var traceEvent = trace.GetTraces().Single();
            Assert.Equal(TraceLevel.Info, traceEvent.Level);
            Assert.Equal("Some Message", traceEvent.Message);
            Assert.Equal(LogCategories.Function, traceEvent.Source);
        }

        [Fact]
        public void FunctionLogger_Ignores_NonFunctionLogs()
        {
            var trace = new TestTraceWriter(TraceLevel.Info);

            var factoryMock = new Mock<IFunctionTraceWriterFactory>(MockBehavior.Strict);
            factoryMock
                .Setup(f => f.Create("SomeFunction", null))
                .Returns(trace);

            var logger = new FunctionLogger("NotAFunction", factoryMock.Object, (c, l) => true);

            // FunctionName comes from scope
            using (logger.BeginScope(new Dictionary<string, object>
            {
                [ScopeKeys.FunctionName] = "SomeFunction"
            }))
            {
                logger.Log(LogLevel.Information, 0, new FormattedLogValues("Some Message"), null, (s, e) => s.ToString());
            }

            Assert.Empty(trace.GetTraces());
        }
    }
}
