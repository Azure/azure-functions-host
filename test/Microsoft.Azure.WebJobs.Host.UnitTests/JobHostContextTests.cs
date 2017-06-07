// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;


namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    public class JobHostContextTests
    {
        [Fact]
        public void Dispose_Disposes()
        {
            var mockLookup = new Mock<IFunctionIndexLookup>(MockBehavior.Strict);
            var mockExecutor = new Mock<IFunctionExecutor>(MockBehavior.Strict);
            var mockListener = new Mock<IListener>(MockBehavior.Strict);
            var traceWriter = new TestTraceWriter(TraceLevel.Verbose);
            var mockLoggerFactory = new Mock<ILoggerFactory>(MockBehavior.Strict);

            mockListener.Setup(p => p.Dispose());
            mockLoggerFactory.Setup(p => p.Dispose());

            var context = new JobHostContext(mockLookup.Object, mockExecutor.Object, mockListener.Object, traceWriter, null, loggerFactory: mockLoggerFactory.Object);

            Assert.Same(traceWriter, context.Trace);

            context.Dispose();

            Assert.Throws<ObjectDisposedException>(() =>
            {
                context.Trace.Info("Kaboom!");
            });

            // verify that calling Dispose again is a noop
            context.Dispose();

            mockListener.Verify(p => p.Dispose(), Times.Once);
            mockLoggerFactory.Verify(p => p.Dispose(), Times.Once);
        }
    }
}
