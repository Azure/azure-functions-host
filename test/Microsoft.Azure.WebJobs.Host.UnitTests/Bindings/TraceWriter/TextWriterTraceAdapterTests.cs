// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using System.IO;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Bindings
{
    public class TextWriterTraceAdapterTests
    {
        private readonly Mock<TraceWriter> _mockTraceWriter;
        private readonly TextWriterTraceAdapter _adapter;

        public TextWriterTraceAdapterTests()
        {
            _mockTraceWriter = new Mock<TraceWriter>(MockBehavior.Strict, TraceLevel.Verbose);
            _adapter = new TextWriterTraceAdapter(_mockTraceWriter.Object);
        }

        [Fact]
        public void Write_SingleCharacterWrites_BuffersUntilNewline()
        {
            _mockTraceWriter.Setup(p => p.Trace(TraceLevel.Info, null, "Mathew\r\n", null));

            _adapter.Write('M');
            _adapter.Write('a');
            _adapter.Write('t');
            _adapter.Write('h');
            _adapter.Write('e');
            _adapter.Write('w');
            _adapter.WriteLine();

            _mockTraceWriter.VerifyAll();
        }

        [Fact]
        public void Write_VariousWriteOverloads_BuffersUntilNewline()
        {
            _mockTraceWriter.Setup(p => p.Trace(TraceLevel.Info, null, "=====================\r\n", null));
            _mockTraceWriter.Setup(p => p.Trace(TraceLevel.Info, null, "TestData123456True=====================\r\n", null));
            _mockTraceWriter.Setup(p => p.Trace(TraceLevel.Info, null, "This is a new line\r\n", null));
            _mockTraceWriter.Setup(p => p.Trace(TraceLevel.Info, null, "This is some more text", null));
            _mockTraceWriter.Setup(p => p.Flush());

            _adapter.Write("=====================\r\n");
            _adapter.Write("TestData");
            _adapter.Write(123456);
            _adapter.Write(true);
            _adapter.Write("=====================\r\n");
            _adapter.WriteLine("This is a new line");
            _adapter.Write("This is some more text");

            _adapter.Flush();
        }

        [Fact]
        public void Flush_FlushesRemainingBuffer()
        {
            _mockTraceWriter.Setup(p => p.Trace(TraceLevel.Info, null, "This is a test", null));
            _mockTraceWriter.Setup(p => p.Flush());

            _adapter.Write("This");
            _adapter.Write(" is ");
            _adapter.Write("a ");
            _adapter.Write("test");
            _adapter.Flush();

            _mockTraceWriter.VerifyAll();
        }
    }
}
