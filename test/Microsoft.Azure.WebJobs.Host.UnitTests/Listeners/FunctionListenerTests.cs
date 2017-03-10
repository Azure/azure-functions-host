// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Moq;
using Xunit;
using System.Collections.ObjectModel;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Listeners
{
    public class FunctionListenerTests
    {
        CancellationToken ct = default(CancellationToken);

        FunctionDescriptor fd = new FunctionDescriptor()
        {
            ShortName = "testfunc"
        };

        [Fact]
        public async Task FunctionListener_Throws_IfUnhandledListenerExceptionOnStartAsync()
        {
            var trace = new TestTraceWriter(TraceLevel.Error);
            Mock<IListener> badListener = new Mock<IListener>(MockBehavior.Strict);
            badListener.Setup(bl => bl.StartAsync(It.IsAny<CancellationToken>()))
                .Throws(new Exception("listener"));
            var listener = new FunctionListener(badListener.Object, fd, trace);

            var e = await Assert.ThrowsAsync<FunctionListenerException>(async () => await listener.StartAsync(ct));
            var exc = trace.Traces[0].Exception as FunctionException;
            Assert.Equal("testfunc", exc.MethodName);
            Assert.False(exc.Handled);
            badListener.VerifyAll();
        }

        [Fact]
        public async Task FunctionListener_DoesNotThrow_IfHandled()
        {
            HandlingTraceWriter trace = new HandlingTraceWriter(TraceLevel.Error, (te) => (te.Exception as RecoverableException).Handled = true);
            Mock<IListener> badListener = new Mock<IListener>(MockBehavior.Strict);
            badListener.Setup(bl => bl.StartAsync(It.IsAny<CancellationToken>()))
                .Throws(new Exception("listener"));
            var listener = new FunctionListener(badListener.Object, fd, trace);

            await listener.StartAsync(ct);

            Assert.Equal("The listener for function 'testfunc' was unable to start.", trace.Traces[0].Message);
            var exc = trace.Traces[0].Exception as FunctionException;
            Assert.Equal("testfunc", exc.MethodName);
            Assert.True(exc.Handled);
            badListener.VerifyAll();
        }

        [Fact]
        public async Task FunctionListener_DoesNotStop_IfNotStarted()
        {
            HandlingTraceWriter trace = new HandlingTraceWriter(TraceLevel.Error, (te) => (te.Exception as RecoverableException).Handled = true);
            Mock<IListener> badListener = new Mock<IListener>(MockBehavior.Strict);
            badListener.Setup(bl => bl.StartAsync(It.IsAny<CancellationToken>()))
                .Throws(new Exception("listener"));
            var listener = new FunctionListener(badListener.Object, fd, trace);

            await listener.StartAsync(ct);
            // these should do nothing, as function listener had an exception on start
            await listener.StopAsync(ct);
            await listener.StopAsync(ct);
            await listener.StopAsync(ct);

            // ensure that badListener.StopAsync is not called on a disabled function listener
            badListener.VerifyAll();
        }

        [Fact]
        public async Task FunctionListener_RunsStop_IfStarted()
        {
            HandlingTraceWriter trace = new HandlingTraceWriter(TraceLevel.Error, (te) => (te.Exception as RecoverableException).Handled = true);
            Mock<IListener> goodListener = new Mock<IListener>(MockBehavior.Strict);
            goodListener.Setup(bl => bl.StartAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(false));
            goodListener.Setup(bl => bl.StopAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(false));
            var listener = new FunctionListener(goodListener.Object, fd, trace);

            await listener.StartAsync(ct);
            await listener.StopAsync(ct);

            Assert.Empty(trace.Traces);

            goodListener.VerifyAll();
        }

        private class HandlingTraceWriter : TraceWriter
        {
            public Collection<TraceEvent> Traces = new Collection<TraceEvent>();
            public Action<TraceEvent> _handler;

            public HandlingTraceWriter(TraceLevel level, Action<TraceEvent> handler) : base(level)
            {
                _handler = handler;
            }

            public override void Trace(TraceEvent traceEvent)
            {
                Traces.Add(traceEvent);
                _handler(traceEvent);
            }
        }
    }
}
