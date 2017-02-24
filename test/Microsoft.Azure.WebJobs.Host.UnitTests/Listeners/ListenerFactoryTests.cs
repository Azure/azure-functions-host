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
    public class ListenerFactoryTests
    {
        CancellationToken ct = default(CancellationToken);

        private async Task<IListener> GetListener(TraceWriter trace)
        {
            var fd = new FunctionDescriptor()
            {
                ShortName = "testfunc"
            };

            Mock<IListener> badListener = new Mock<IListener>(MockBehavior.Strict);
            badListener.Setup(bl => bl.StartAsync(It.IsAny<CancellationToken>()))
                .Throws(new Exception("listener"));

            Mock<ITriggerBinding> triggerBinding = new Mock<ITriggerBinding>(MockBehavior.Strict);
            triggerBinding.Setup(tb => tb.CreateListenerAsync(It.IsAny<ListenerFactoryContext>()))
                .ReturnsAsync(badListener.Object);
            var lf = new FunctionIndexer.ListenerFactory(fd, null, triggerBinding.Object, trace);

            return await lf.CreateAsync(ct);
        }

        [Fact]
        public async Task GeneratedListener_Throws_IfListenerExceptionOnStartAsync()
        {
            var trace = new TestTraceWriter(TraceLevel.Error);
            var listener = await GetListener(trace);
            var e = await Assert.ThrowsAsync<FunctionListenerException>(async () => await listener.StartAsync(ct));
            var exc = trace.Traces[0].Exception as FunctionException;
            Assert.Equal("testfunc", exc.MethodName);
            Assert.False(exc.Handled);
        }

        [Fact]
        public async Task GeneratedListener_DoesNotThrow_IfHandled()
        {
            HandlingTraceWriter trace = new HandlingTraceWriter(TraceLevel.Error, (te) => (te.Exception as RecoverableException).Handled = true);
            var listener = await GetListener(trace);
            await listener.StartAsync(ct);
            Assert.Equal("The listener for function 'testfunc' was unable to start.", trace.Traces[0].Message);
            var exc = trace.Traces[0].Exception as FunctionException;
            Assert.Equal("testfunc", exc.MethodName);
            Assert.True(exc.Handled);
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
