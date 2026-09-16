using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
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
                IFunctionInvocationDispatcherFactory factory = Fixture.Host.Services.GetService<IFunctionInvocationDispatcherFactory>();
                fd = factory.GetFunctionDispatcher() as RpcFunctionInvocationDispatcher;
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
            private readonly IScriptEventManager _scriptEventManager = new ScriptEventManager();

            public void Publish(ScriptEvent scriptEvent)
            {
                try
                {
                    _scriptEventManager.Publish(scriptEvent);
                }
                catch (ObjectDisposedException)
                {
                }
            }

            public IDisposable Subscribe(IObserver<ScriptEvent> observer) => _scriptEventManager.Subscribe(observer);

            public void Dispose() => ((IDisposable)_scriptEventManager).Dispose();
        }

        internal class TestServerDuplexChannelRegistry : ServerDuplexChannelRegistry
        {
            private readonly TimeSpan _delay;

            public TestServerDuplexChannelRegistry(TimeSpan delay)
            {
                _delay = delay;
            }

            protected override ServerDuplexChannel CreateChannel()
            {
                return new ServerDuplexChannel(
                    new DelayedChannel<StreamingMessage>(_delay, ServerDuplexChannel.WorkerToHostOptions),
                    Channel.CreateUnbounded<StreamingMessage>(ServerDuplexChannel.HostToWorkerOptions));
            }

            public class DelayedChannel<T> : Channel<T>
            {
                public DelayedChannel(TimeSpan delay, UnboundedChannelOptions options)
                {
                    var toWrap = Channel.CreateUnbounded<T>(options);
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

                public override bool TryComplete(Exception error = null) => _inner.TryComplete(error);

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
