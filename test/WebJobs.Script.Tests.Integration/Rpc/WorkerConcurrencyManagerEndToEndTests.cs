using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.Eventing;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class WorkerConcurrencyManagerEndToEndTests : IClassFixture<WorkerConcurrencyManagerEndToEndTests.TestFixture>
    {
        public WorkerConcurrencyManagerEndToEndTests(TestFixture fixture)
        {
            Fixture = fixture;
        }

        public TestFixture Fixture { get; set; }

        [Fact]
        public async Task WorkerStatus_NewWorkerAdded()
        {
            RpcFunctionInvocationDispatcher fd = null;
            IEnumerable<IRpcWorkerChannel> channels = null;

            await TestHelpers.Await(async () =>
            {
                fd = Fixture.JobHost.FunctionDispatcher as RpcFunctionInvocationDispatcher;
                channels = await fd.GetInitializedWorkerChannelsAsync();
                return channels.Count() == 2;
            }, pollingInterval: 1000, timeout: 120 * 1000);
        }

        public class TestFixture : ScriptHostEndToEndTestFixture
        {
            // Latency > 1s
            public TestFixture() : base(@"TestScripts\Node", "node", RpcWorkerConstants.NodeLanguageWorkerName,
                startHost: true, functions: new[] { "HttpTrigger" },
                addWorkerConcurrency: true,
                addWorkerDelay: TimeSpan.FromSeconds(2))
            {
            }
        }

        internal class TestScriptEventManager : IScriptEventManager, IDisposable
        {
            private readonly IScriptEventManager _scriptEventManager;
            private readonly TimeSpan _delay;

            public TestScriptEventManager(TimeSpan delay)
            {
                _scriptEventManager = new ScriptEventManager();
                _delay = delay;
            }

            public void Publish(ScriptEvent scriptEvent)
            {
                try
                {
                    _scriptEventManager.Publish(scriptEvent);
                } 
                catch (ObjectDisposedException)
                {
                    // Do no throw ObjectDisposedException
                }
            }

            public IDisposable Subscribe(IObserver<ScriptEvent> observer) => _scriptEventManager.Subscribe(observer);

            public void Dispose() => ((IDisposable)_scriptEventManager).Dispose();
            public bool TryAddWorkerState<T>(string workerId, T state)
            {
                // Swap for a channel that imposes a delay into the pipe
                if (typeof(T) == typeof(Channel<InboundGrpcEvent>) && _delay > TimeSpan.Zero)
                {
                    state = (T)(object)(new DelayedOutboundChannel<InboundGrpcEvent>(_delay));
                }
                return _scriptEventManager.TryAddWorkerState(workerId, state);
            }

            public bool TryGetWorkerState<T>(string workerId, out T state)
                => _scriptEventManager.TryGetWorkerState(workerId, out state);

            public bool TryRemoveWorkerState<T>(string workerId, out T state)
                => _scriptEventManager.TryRemoveWorkerState(workerId, out state);


            public class DelayedOutboundChannel<T> : Channel<T>
            {
                public DelayedOutboundChannel(TimeSpan delay)
                {
                    var toWrap = Channel.CreateUnbounded<T>(GrpcEventExtensions.OutboundOptions);
                    Reader = toWrap.Reader;
                    Writer = new DelayedChannelWriter<T>(toWrap.Writer, delay);
                }
            }

            public class DelayedChannelWriter<T> : ChannelWriter<T>
            {
                private readonly TimeSpan _delay;
                private readonly ChannelWriter<T> _inner;

                public DelayedChannelWriter(ChannelWriter<T> toWrap, TimeSpan delay) => (_inner, _delay) = (toWrap, delay);

                public override bool TryWrite(T item) => false; // Always fail, so we bounce to WriteAsync
                public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default) => _inner.WaitToWriteAsync(cancellationToken);

                public override async ValueTask WriteAsync(T item, CancellationToken cancellationToken = default)
                {
                    await Task.Delay(_delay, cancellationToken);
                    await _inner.WriteAsync(item, cancellationToken);
                }
            }
        }
    }
}
