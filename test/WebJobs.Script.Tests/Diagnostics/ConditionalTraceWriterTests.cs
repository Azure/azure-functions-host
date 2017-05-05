// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class ConditionalTraceWriterTests
    {
        [Fact]
        public void Dispose_Disposes_If_IDisposable()
        {
            var disposableTraceWriter = new DisposableTraceWriter();
            var traceWriter = new ConditionalTraceWriter(disposableTraceWriter, t => true);

            Assert.False(disposableTraceWriter.IsDisposed);
            traceWriter.Dispose();
            Assert.True(disposableTraceWriter.IsDisposed);
        }

        [Fact]
        public void Dispose_Ignores_IfNot_IDisposable()
        {
            var testTraceWriter = new TestTraceWriter(TraceLevel.Verbose);
            Assert.IsNotType<IDisposable>(testTraceWriter);
            var traceWriter = new ConditionalTraceWriter(testTraceWriter, t => true);

            // This shouldn't crash
            traceWriter.Dispose();
        }
    }
}
