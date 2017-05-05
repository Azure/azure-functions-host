// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.Azure.WebJobs.Host;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class CompositeTraceWriterTests
    {
        [Fact]
        public void Trace_CallsInnerWriterTrace()
        {
            var traceWriter = new TestTraceWriter(TraceLevel.Verbose);
            var compositeWriter = new CompositeTraceWriter(new[] { traceWriter });

            string message = "Test trace";
            compositeWriter.Verbose(message);

            TraceEvent trace = traceWriter.Traces.FirstOrDefault();

            Assert.NotNull(trace);
            Assert.Equal(TraceLevel.Verbose, trace.Level);
            Assert.Equal(message, trace.Message);
        }

        [Fact]
        public void Trace_RespectsInnerWriterLevel()
        {
            var traceWriter = new TestTraceWriter(TraceLevel.Error);
            var compositeWriter = new CompositeTraceWriter(new[] { traceWriter });

            string message = "Test trace";
            compositeWriter.Verbose(message);
            compositeWriter.Error(message);

            Assert.Equal(1, traceWriter.Traces.Count);

            TraceEvent trace = traceWriter.Traces.First();

            Assert.Equal(TraceLevel.Error, trace.Level);
            Assert.Equal(message, trace.Message);
        }

        [Fact]
        public void Flush_FlushesInternalWriters()
        {
            var traceWriter = new TestTraceWriter(TraceLevel.Error);
            var compositeWriter = new CompositeTraceWriter(new[] { traceWriter });

            string message = "Test trace";
            compositeWriter.Error(message);
            compositeWriter.Flush();

            Assert.True(traceWriter.Flushed);
        }

        [Fact]
        public void Dispose_Disposes_If_IDisposable()
        {
            var disposableTraceWriter1 = new DisposableTraceWriter();
            var disposableTraceWriter2 = new DisposableTraceWriter();
            var testTraceWriter = new TestTraceWriter(TraceLevel.Verbose);
            Assert.IsNotType<IDisposable>(testTraceWriter);

            var traceWriter = new CompositeTraceWriter(new TraceWriter[] { disposableTraceWriter1, testTraceWriter, disposableTraceWriter2 });

            Assert.False(disposableTraceWriter1.IsDisposed);
            Assert.False(disposableTraceWriter2.IsDisposed);
            traceWriter.Dispose();
            Assert.True(disposableTraceWriter1.IsDisposed);
            Assert.True(disposableTraceWriter2.IsDisposed);
        }
    }
}
