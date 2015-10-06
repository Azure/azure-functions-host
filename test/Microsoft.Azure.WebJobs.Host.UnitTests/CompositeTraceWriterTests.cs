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
            _mockTraceWriter.Setup(p => p.Trace(It.Is<TraceEvent>(q => q.Level == TraceLevel.Warning && q.Source == "TestSource" && q.Message == "Test Warning" && q.Exception == null)));
            _mockTraceWriter.Setup(p => p.Trace(It.Is<TraceEvent>(q => q.Level == TraceLevel.Error && q.Source == "TestSource" && q.Message == "Test Error" && q.Exception == null)));
            Exception ex = new Exception("Kaboom!");
            _mockTraceWriter.Setup(p => p.Trace(It.Is<TraceEvent>(q => q.Level == TraceLevel.Error && q.Source == "TestSource" && q.Message == "Test Error With Exception" && q.Exception == ex)));

            _mockTextWriter.Setup(p => p.WriteLine("Test Information"));
            _mockTextWriter.Setup(p => p.WriteLine("Test Warning"));
            _mockTextWriter.Setup(p => p.WriteLine("Test Error"));
            _mockTextWriter.Setup(p => p.WriteLine("Test Error With Exception"));
            _mockTextWriter.Setup(p => p.WriteLine(ex.ToDetails()));

            _traceWriter.Info("Test Information", source: "TestSource");  // don't expect this to be logged
            _traceWriter.Warning("Test Warning", source: "TestSource");
            _traceWriter.Error("Test Error", source: "TestSource");
            _traceWriter.Error("Test Error With Exception", ex, source: "TestSource");

            _mockTextWriter.VerifyAll();
            _mockTraceWriter.VerifyAll();
        }

        [Fact]
        public void Trace_CustomProperties()
        {
            TraceEvent capturedEvent = null;
            _mockTraceWriter.Setup(p => p.Trace(It.IsAny<TraceEvent>()))
                .Callback<TraceEvent>(p =>
                {
                    capturedEvent = p;
                });
            _mockTextWriter.Setup(p => p.WriteLine("Test Warning"));

            TraceEvent traceEvent = new TraceEvent(TraceLevel.Warning, "Test Warning", "Test Source");
            traceEvent.Properties.Add("Test", "Test Property");

            _traceWriter.Trace(traceEvent);

            _mockTraceWriter.VerifyAll();
            _mockTextWriter.VerifyAll();

            Assert.Same(traceEvent, capturedEvent);
            Assert.Equal("Test Property", capturedEvent.Properties["Test"]);
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
