// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    public class ConsoleTraceWriterTests
    {
        private readonly Mock<TraceWriter> _mockTraceWriter;
        private readonly Mock<TextWriter> _mockTextWriter;
        private readonly ConsoleTraceWriter _traceWriter;

        public ConsoleTraceWriterTests()
        {
            _mockTextWriter = new Mock<TextWriter>(MockBehavior.Strict);
            _mockTraceWriter = new Mock<TraceWriter>(MockBehavior.Strict, TraceLevel.Info);
            _traceWriter = new ConsoleTraceWriter(_mockTraceWriter.Object, TraceLevel.Info, _mockTextWriter.Object);
        }

        [Fact]
        public void Trace_FiltersBySourceAndLevel()
        {
            _mockTextWriter.Setup(p => p.WriteLine("Test Information"));

            _mockTraceWriter.Setup(p => p.Trace(TraceLevel.Info, Host.TraceSource.Host, "Test Information", null));
            _mockTraceWriter.Setup(p => p.Trace(TraceLevel.Info, "TestSource", "Test Information With Source", null));
            _mockTraceWriter.Setup(p => p.Trace(TraceLevel.Info, null, "Test Information No Source", null));
            _mockTraceWriter.Setup(p => p.Trace(TraceLevel.Warning, null, "Test Warning", null));
            _mockTraceWriter.Setup(p => p.Trace(TraceLevel.Error, null, "Test Error", null));
            Exception ex = new Exception("Kaboom!");
            _mockTraceWriter.Setup(p => p.Trace(TraceLevel.Error, null, "Test Error With Exception", ex));

            // expect these to be logged to both
            _traceWriter.Info("Test Information", Host.TraceSource.Host);

            // don't expect these to be logged to text writer (based on level filter)
            _traceWriter.Warning("Test Warning");
            _traceWriter.Error("Test Error");
            _traceWriter.Error("Test Error With Exception", ex);

            // don't expect these to be logged to text writer (based on source filter)
            _traceWriter.Info("Test Information With Source", "TestSource");
            _traceWriter.Info("Test Information No Source");

            _mockTextWriter.VerifyAll();
            _mockTraceWriter.VerifyAll();
        }

        [Theory]
        [InlineData(TraceSource.Host, TraceLevel.Info, TraceLevel.Info)]
        [InlineData(TraceSource.Host, TraceLevel.Warning, TraceLevel.Warning)]
        [InlineData(TraceSource.Host, TraceLevel.Error, TraceLevel.Error)]
        [InlineData(TraceSource.Indexing, TraceLevel.Info, TraceLevel.Info)]
        [InlineData(TraceSource.Indexing, TraceLevel.Warning, TraceLevel.Warning)]
        [InlineData(TraceSource.Indexing, TraceLevel.Error, TraceLevel.Error)]
        [InlineData(TraceSource.Execution, TraceLevel.Info, TraceLevel.Info)]
        [InlineData(TraceSource.Execution, TraceLevel.Warning, TraceLevel.Verbose)]
        [InlineData(TraceSource.Execution, TraceLevel.Error, TraceLevel.Verbose)]
        [InlineData("Custom", TraceLevel.Info, TraceLevel.Verbose)]
        [InlineData("Custom", TraceLevel.Warning, TraceLevel.Verbose)]
        [InlineData("Custom", TraceLevel.Error, TraceLevel.Verbose)]
        public void MapTraceLevel_PerformsCorrectMapping(string source, TraceLevel level, TraceLevel expected)
        {
            Assert.Equal(expected, ConsoleTraceWriter.MapTraceLevel(source, level));
        }
    }
}
