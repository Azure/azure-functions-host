// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Azure.WebJobs.Host.Executors;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    public class CompositeTraceWriterTests
    {
        private readonly Mock<TraceWriter> _mockTraceWriter;
        private readonly Mock<TextWriter> _mockTextWriter;
        private readonly CompositeTraceWriter _traceWriter;

        public CompositeTraceWriterTests()
        {
            _mockTextWriter = new Mock<TextWriter>(MockBehavior.Strict);
            _mockTraceWriter = new Mock<TraceWriter>(MockBehavior.Strict, TraceLevel.Warning);
            _traceWriter = new CompositeTraceWriter(_mockTraceWriter.Object, _mockTextWriter.Object);
        }

        [Fact]
        public void Trace_DelegatesToInnerTraceWriterAndTextWriter()
        {
            _mockTraceWriter.Setup(p => p.Trace(TraceLevel.Warning, "TestSource", "Test Warning", null));
            _mockTraceWriter.Setup(p => p.Trace(TraceLevel.Error, "TestSource", "Test Error", null));
            Exception ex = new Exception("Kaboom!");
            _mockTraceWriter.Setup(p => p.Trace(TraceLevel.Error, "TestSource", "Test Error With Exception", ex));

            _mockTextWriter.Setup(p => p.WriteLine("Test Information"));
            _mockTextWriter.Setup(p => p.WriteLine("Test Warning"));
            _mockTextWriter.Setup(p => p.WriteLine("Test Error"));
            _mockTextWriter.Setup(p => p.WriteLine("Test Error With Exception"));
            _mockTextWriter.Setup(p => p.WriteLine(ex.ToDetails()));

            _traceWriter.Info("Test Information", source: "TestSource");  // don't expect this to be logged
            _traceWriter.Warning("Test Warning", source: "TestSource");
            _traceWriter.Error("Test Error", null, source: "TestSource");
            _traceWriter.Error("Test Error With Exception", ex, source: "TestSource");

            _mockTextWriter.VerifyAll();
            _mockTraceWriter.VerifyAll();
        }

        [Fact]
        public void Flush_FlushesInnerTraceWriterAndTextWriter()
        {
            _mockTraceWriter.Setup(p => p.Flush());
            _mockTextWriter.Setup(p => p.Flush());

            _traceWriter.Flush();

            _mockTextWriter.VerifyAll();
            _mockTraceWriter.VerifyAll();
        }
    }
}
