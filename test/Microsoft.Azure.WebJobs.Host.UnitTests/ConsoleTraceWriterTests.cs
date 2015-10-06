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
        private readonly Mock<TraceWriter> _mockTraceWriter2;
        private readonly Mock<TextWriter> _mockTextWriter;
        private readonly ConsoleTraceWriter _traceWriter;
        private readonly JobHostTraceConfiguration _traceConfig;

        public ConsoleTraceWriterTests()
        {
            _mockTextWriter = new Mock<TextWriter>(MockBehavior.Strict);
            _mockTraceWriter = new Mock<TraceWriter>(MockBehavior.Strict, TraceLevel.Info);
            _mockTraceWriter2 = new Mock<TraceWriter>(MockBehavior.Strict, TraceLevel.Error);
            _traceConfig = new JobHostTraceConfiguration
            {
                ConsoleLevel = TraceLevel.Info
            };
            _traceConfig.Tracers.Add(_mockTraceWriter.Object);
            _traceConfig.Tracers.Add(_mockTraceWriter2.Object);
            _traceWriter = new ConsoleTraceWriter(_traceConfig, _mockTextWriter.Object);
        }

        [Fact]
        public void Trace_FiltersBySourceAndLevel()
        {
            _mockTextWriter.Setup(p => p.WriteLine("Test Information"));

            _mockTraceWriter.Setup(p => p.Trace(It.Is<TraceEvent>(
                q => q.Level == TraceLevel.Info && 
                q.Source == Host.TraceSource.Host &&
                q.Message == "Test Information")));
            _mockTraceWriter.Setup(p => p.Trace(It.Is<TraceEvent>(
                q => q.Level == TraceLevel.Info &&
                q.Source == "TestSource" &&
                q.Message == "Test Information With Source")));
            _mockTraceWriter.Setup(p => p.Trace(It.Is<TraceEvent>(
                q => q.Level == TraceLevel.Info &&
                q.Source == null &&
                q.Message == "Test Information No Source")));
            _mockTraceWriter.Setup(p => p.Trace(It.Is<TraceEvent>(q => 
                q.Level == TraceLevel.Warning &&
                q.Source == null &&
                q.Message == "Test Warning")));
            _mockTraceWriter.Setup(p => p.Trace(It.Is<TraceEvent>(q =>
                q.Level == TraceLevel.Error &&
                q.Source == null &&
                q.Message == "Test Error")));
            _mockTraceWriter2.Setup(p => p.Trace(It.Is<TraceEvent>(q =>
                q.Level == TraceLevel.Error &&
                q.Source == null &&
                q.Message == "Test Error")));
            Exception ex = new Exception("Kaboom!");
            _mockTraceWriter.Setup(p => p.Trace(It.Is<TraceEvent>(q =>
                q.Level == TraceLevel.Error &&
                q.Source == null &&
                q.Message == "Test Error With Exception")));
            _mockTraceWriter2.Setup(p => p.Trace(It.Is<TraceEvent>(q =>
                q.Level == TraceLevel.Error &&
                q.Source == null &&
                q.Message == "Test Error With Exception")));

            // expect this to be logged to both tracer1 and textWriter
            _traceWriter.Info("Test Information", Host.TraceSource.Host);

            // don't expect these to be logged to text writer (based on level filter)
            // expect the errors to be logged to both traceWriters
            _traceWriter.Warning("Test Warning");
            _traceWriter.Error("Test Error");
            _traceWriter.Error("Test Error With Exception", ex);

            // don't expect these to be logged to text writer (based on source filter)
            // only written to traceWriter1
            _traceWriter.Info("Test Information With Source", "TestSource");
            _traceWriter.Info("Test Information No Source");

            _mockTextWriter.VerifyAll();
            _mockTraceWriter.VerifyAll();
            _mockTraceWriter2.VerifyAll();
        }

        [Fact]
        public void Flush_FlushesInnerWriters()
        {
            _mockTextWriter.Setup(p => p.Flush());
            _mockTraceWriter.Setup(p => p.Flush());
            _mockTraceWriter2.Setup(p => p.Flush());

            _traceWriter.Flush();

            _mockTextWriter.VerifyAll();
            _mockTraceWriter.VerifyAll();
            _mockTraceWriter2.VerifyAll();
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

        [Fact]
        public void ConsoleLevel_CanBeChangedWhileRunning()
        {
            _mockTextWriter.Setup(p => p.WriteLine("Test Information"));
            _mockTraceWriter.Setup(p => p.Trace(It.Is<TraceEvent>(q =>
                q.Level == TraceLevel.Info &&
                q.Source == null &&
                q.Message == "Test Information")));

            _traceWriter.Info("Test Information");

            _traceConfig.ConsoleLevel = TraceLevel.Verbose;
            _traceWriter.Info("Test Information");

            _mockTextWriter.VerifyAll();
            _mockTraceWriter.VerifyAll();
        }
    }
}
